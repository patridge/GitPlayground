using Mono.Options;
using Nito.AsyncEx;
using Octokit;
using System;
using System.Configuration;
using System.Linq;
using System.Threading.Tasks;

namespace GitPlayground
{
    class Program
    {
        static void Main(string[] args)
        {
            AsyncContext.Run(() => MainAsync(args));
            Console.Read();
        }

        static async void MainAsync(string[] args)
        {
            const string gitHubPersonalAccessTokenKey = "gitHubPersonalAccessToken";
            string gitHubPersonalAccessToken = null;
            string gitParentRepoName = null;
            string gitParentRepoOwner = null;
            string gitParentRepoBranchName = "master";
            string gitSubmoduleRepoName = null;
            string gitSubmoduleRepoOwner = null;
            string gitSubmoduleRepoBranchName = "master";
            string gitPullRequestOwner = null;

            OptionSet options = null;
            options = new OptionSet {
                { "h|?|help", "Displays help", v => ShowHelp(options) },
                { "token=", $"GitHub personal access token override of personalSettings.config '{gitHubPersonalAccessTokenKey}' appSettings value.", token => {
                    gitHubPersonalAccessToken = token;
                }},
                { "owner=", "Repo owner for parent and submodule and pull request (unless overridden for submodule)", owner => gitParentRepoOwner = owner },
                { "parent=", "Parent repo name with submodules to analyze", parent => gitParentRepoName = parent },
                { "parentBranch=", "Branch name within parent repo", parentBranch =>  gitParentRepoBranchName = parentBranch },
                { "sub=", "Submodule repo name to analyze in parent repo", sub => gitSubmoduleRepoName = sub },
                { "subOwner=", "Submodule repo owner, if distince from parent owner", owner => gitSubmoduleRepoOwner = owner },
                { "subBranch=", "Branch name within submodule repo", subBranch =>  gitSubmoduleRepoBranchName = subBranch },
                { "pullOwner=", "Owner of pull request to be created", pullOwner => gitPullRequestOwner = pullOwner },
            };

            try
            {
                // Parse command line
                options.Parse(args);
                // Fall back to personalSettings.config value for token
                gitHubPersonalAccessToken = gitHubPersonalAccessToken ?? (gitHubPersonalAccessToken = ConfigurationManager.AppSettings.Get(gitHubPersonalAccessTokenKey));
                gitSubmoduleRepoOwner = gitSubmoduleRepoOwner ?? gitParentRepoOwner;
                gitPullRequestOwner = gitPullRequestOwner ?? gitParentRepoOwner;
            }
            catch (OptionException e)
            {
                Console.WriteLine("Failed to parse command line correctly.");
                ShowHelp(options);
                return;
            }

            var parentRepoInfo = new RepoQueryInfo
            {
                Owner = gitParentRepoOwner,
                Name = gitParentRepoName,
                BranchName = gitParentRepoBranchName,
            };
            var submoduleRepoInfo = new RepoQueryInfo
            {
                Owner = gitSubmoduleRepoOwner,
                Name = gitSubmoduleRepoName,
                BranchName = gitSubmoduleRepoBranchName,
            };

            await DissectSubmoduleRepo(gitHubPersonalAccessToken, gitPullRequestOwner, parentRepoInfo, submoduleRepoInfo);
        }

        static async Task DissectSubmoduleRepo(string accessToken, string pullRequestOwner, RepoQueryInfo parentRepoInfo, RepoQueryInfo submoduleRepoInfo)
        {
            var gitHubClient = new GitHubClient(new ProductHeaderValue("submodule-helper"));
            var tokenAuth = new Octokit.Credentials(accessToken);
            gitHubClient.Credentials = tokenAuth;
            var repoClient = new RepositoriesClient(new ApiConnection(gitHubClient.Connection));

            var parentRepoId = (await gitHubClient.Repository.Get(parentRepoInfo.Owner, parentRepoInfo.Name)).Id;
            var parentTree = await gitHubClient.Git.Tree.Get(parentRepoId, parentRepoInfo.BranchName);
            var parentBranchLatestSha = parentTree.Sha;
            Console.WriteLine(parentBranchLatestSha);

            var submoduleRepoId = (await gitHubClient.Repository.Get(parentRepoInfo.Owner, submoduleRepoInfo.Name)).Id;
            var submoduleTree = await gitHubClient.Git.Tree.Get(submoduleRepoId, submoduleRepoInfo.BranchName);
            var submoduleBranchLatestSha = submoduleTree.Sha;
            Console.WriteLine(submoduleBranchLatestSha);

            // TODO: Get parent .gitmodules file
            // TODO: Find `url = https://github.com/{owner}/{name}.git` in the file
            // TODO: Find submodule path in nearest above `[submodule "{path}"]` line
            var submodulePath = "subs"; // NOTE: currently hard-coded to patridge/GitPlayground-SampleParent test repo value.
            // TODO: ??? Determine current submodule target hash (not sure how to access yet via Octokit)

            // TODO: Determine next available `pull-{#}` branch for {pullRequestOwner}/{parentRepoInfo.Name}
            // TODO: Create `pull-{#}` branch for {pullRequestOwner}/{parentRepoInfo.Name}

            // NOTE: Running same submodule update n times results in n commits. Need to figure out if needed via .gitmodules download first.
            var updateParentTree = new NewTree { BaseTree = parentBranchLatestSha };
            updateParentTree.Tree.Add(new NewTreeItem
            {
                Mode = FileMode.Submodule,
                Sha = submoduleBranchLatestSha,
                Path = submodulePath,
                Type = TreeType.Commit,
            });
            var newParentTree = await gitHubClient.Git.Tree.Create(pullRequestOwner, parentRepoInfo.Name, updateParentTree);
            var newCommit = new NewCommit($"Update submodule {submoduleRepoInfo.Owner}/{submoduleRepoInfo.Name}", newParentTree.Sha, parentBranchLatestSha);
            var commit = await gitHubClient.Git.Commit.Create(pullRequestOwner, parentRepoInfo.Name, newCommit);
            // TODO: Figure out how to get this value without the string concat hack.
            var parentBranchRef = $"heads/{parentRepoInfo.BranchName}";
            await gitHubClient.Git.Reference.Update(pullRequestOwner, parentRepoInfo.Name, parentBranchRef, new ReferenceUpdate(commit.Sha));

            // TODO: Create pull request from {pullRequestOwner}/{parentRepoInfo.Name} to {parentRepoInfo.Owner}/{parentRepoInfo.Name}
        }

        static void ShowHelp(OptionSet os)
        {
            Console.WriteLine("GitPlayground [options]");
            os.WriteOptionDescriptions(Console.Out);
            Environment.Exit(1);
        }
    }
}
