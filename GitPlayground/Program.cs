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

            // Set up for GitHub API access.
            var gitHubClient = new GitHubClient(new ProductHeaderValue("submodule-helper"))
            {
                Credentials = new Octokit.Credentials(gitHubPersonalAccessToken),
            };
            // If an owner for the Pull Request wasn't provided, fall back to the authenticated user.
            if (gitPullRequestOwner == null)
            {
                var currentUser = await gitHubClient.User.Current();
                gitPullRequestOwner = currentUser.Name;
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

            await DissectSubmoduleRepo(gitHubClient, gitPullRequestOwner, parentRepoInfo, submoduleRepoInfo);
        }

        static async Task DissectSubmoduleRepo(GitHubClient gitHubClient, string pullRequestOwner, RepoQueryInfo parentRepoInfo, RepoQueryInfo submoduleRepoInfo)
        {
            var apiConnection = new ApiConnection(gitHubClient.Connection);
            var repoClient = new RepositoriesClient(apiConnection);
            var forksClient = new RepositoryForksClient(apiConnection);
            var referencesClient = new ReferencesClient(apiConnection);
            var pullRequestsClient = new PullRequestsClient(apiConnection);

            // Get parent repo details.
            var parentRepoId = (await gitHubClient.Repository.Get(parentRepoInfo.Owner, parentRepoInfo.Name)).Id;
            var parentBranchLatestSha = (await gitHubClient.Git.Tree.Get(parentRepoId, parentRepoInfo.BranchName)).Sha;
            Console.WriteLine(parentBranchLatestSha);
            Console.WriteLine($"Parent repo branch latest hash: {parentBranchLatestSha}");

            // Get latest submodule repo details
            var submoduleRepoId = (await gitHubClient.Repository.Get(parentRepoInfo.Owner, submoduleRepoInfo.Name)).Id;
            var submoduleBranchLatestSha = (await gitHubClient.Git.Tree.Get(submoduleRepoId, submoduleRepoInfo.BranchName)).Sha;
            Console.WriteLine($"Submodule repo branch latest hash: {submoduleBranchLatestSha}");

            // TODO: Get parent .gitmodules file
            var gitmodulesContent = await repoClient.Content.GetAllContents(parentRepoId, ".gitmodules");
            if (gitmodulesContent.Count != 1) { throw new InvalidOperationException("Did not find a .gitmodules file in the parent repo."); }
            // ASSUMPTION: submodule path ends with submodule repo name.
            var gitmodulesGroupForSubmodule = gitmodulesContent[0].Content
                .Split(new[] { '\n', '\r', }, StringSplitOptions.RemoveEmptyEntries) // Split by line breaks, removing empties for doubles on `\r\n`
                .Select((line, i) => new { Index = i, Line = line }) // Index all the lines
                .GroupBy(indexedLine => Math.Round(indexedLine.Index / 3.0)) // Group every 3 lines
                .Select(groupedIndexedLines => groupedIndexedLines.Select(indexedLine => indexedLine.Line))
                .FirstOrDefault(indexedGroup => indexedGroup.First().EndsWith($"{submoduleRepoInfo.Name}\"]"));
            if (gitmodulesGroupForSubmodule == null) { throw new InvalidOperationException("Did not find requested submodule repo in parent repo's .gitmodules file."); }
            var submodulePath = new string(gitmodulesGroupForSubmodule.First().SkipWhile(c => c != '"').Skip(1).TakeWhile(c => c != '"').ToArray());
            Console.WriteLine(submodulePath);

            // TODO: ??? Determine current submodule target hash (not sure how to access yet via Octokit)
            // TODO: Figure out if update needed based on submodule repo latest hash vs. parent repo submodule target hash.
            Console.WriteLine($"WARNING: Not currently verifying if submodule actually needs updating. (Currently unable to determine submodule target hash.)");

            var pullRequestRepoName = parentRepoInfo.Name;
            var parentForks = await forksClient.GetAll(parentRepoId);
            var isPullRequestOwnerSameAsParentRepoOwner = pullRequestOwner != parentRepoInfo.Owner;
            if (isPullRequestOwnerSameAsParentRepoOwner)
            {
                // Deal with user's parent repo fork.
                var pullRequestOwnerFork = parentForks.FirstOrDefault(f => f.Owner.Login == pullRequestOwner);
                if (pullRequestOwnerFork != null)
                {
                    // Update PR repo name with PR-owner's fork name (in case it has changed).
                    pullRequestRepoName = pullRequestOwnerFork.Name;
                    // NOTE: It doesn't appear that we need to update an outdated fork to branch from the upstream commit hash.
                }
                else
                {
                    // If PR owner doesn't have fork of repo, make one.
                    // TODO: Handle if user has non-fork repo of the same name as the parent repo.
                    pullRequestOwnerFork = await forksClient.Create(pullRequestOwner, parentRepoInfo.Name, new NewRepositoryFork());
                    // NOTE: If you create a fork on an owner who already has one, you just get back the fork they already had.
                    throw new NotImplementedException("Need to create new parent repo fork for PR owner.");
                }
            }
            else
            {
                Console.WriteLine($"No PR fork needed: parent repo owner matches PR owner; just branch and PR in parent repo.");
            }
            var pullRequestBranchPrefix = "patch-";
            var pullRequestOwnerForkRepoId = (await gitHubClient.Repository.Get(pullRequestOwner, parentRepoInfo.Name)).Id;
            var pullRequestLargestExistingNumber = (await repoClient.Branch.GetAll(pullRequestOwnerForkRepoId))
                .Where(b => b.Name.StartsWith(pullRequestBranchPrefix))
                .Select(b =>
                {
                    var branchSuffix = new string(b.Name.Skip(pullRequestBranchPrefix.Length).ToArray());
                    bool branchSuffixValid = int.TryParse(branchSuffix, out int branchSuffixNumber);
                    return (branchSuffixValid ? (int?)branchSuffixNumber : null);
                })
                .Where(branchNumber => branchNumber != null)
                .OrderByDescending(n => n)
                .FirstOrDefault() ?? 0;
            var pullRequestBranchName = $"{pullRequestBranchPrefix}{pullRequestLargestExistingNumber + 1}";
            Console.WriteLine($"Creating PR fork branch: {pullRequestBranchName}");
            var pullRequestBranch = await referencesClient.Create(pullRequestOwnerForkRepoId, new NewReference($"refs/heads/{pullRequestBranchName}", parentBranchLatestSha));

            // Create commit on parent to update submodule hash target.
            // NOTE: Running same submodule update n times results in n commits until we verify update needed.
            var updateParentTree = new NewTree { BaseTree = parentBranchLatestSha };

            updateParentTree.Tree.Add(new NewTreeItem
            {
                Mode = FileMode.Submodule,
                Sha = submoduleBranchLatestSha,
                Path = submodulePath,
                Type = TreeType.Commit,
            });
            var newParentTree = await gitHubClient.Git.Tree.Create(pullRequestOwnerForkRepoId, updateParentTree);
            var newCommit = new NewCommit($"Update submodule {submoduleRepoInfo.Owner}/{submoduleRepoInfo.Name}", newParentTree.Sha, parentBranchLatestSha);
            var commit = await gitHubClient.Git.Commit.Create(pullRequestOwner, parentRepoInfo.Name, newCommit);
            // TODO: Figure out how to get this value without the string concat hack.
            var pullRequestBranchRef = $"heads/{pullRequestBranchName}";
            await gitHubClient.Git.Reference.Update(pullRequestOwnerForkRepoId, pullRequestBranchRef, new ReferenceUpdate(commit.Sha));

            var parentBranchRef = $"heads/{parentRepoInfo.BranchName}";
            var pullRequestSourceRef = $"{pullRequestBranchRef}";
            if (!isPullRequestOwnerSameAsParentRepoOwner)
            {
                // For a PR, the comparison ref to a different user requires a prefix.
                pullRequestSourceRef = $"{pullRequestOwner}:{pullRequestBranchRef}";
            }
            Console.WriteLine($"{nameof(pullRequestSourceRef)}: {pullRequestSourceRef}");
            //var newPullRequest = await pullRequestsClient.Create(parentRepoId, new NewPullRequest($"Update submodule {submoduleRepoInfo.Owner}/{submoduleRepoInfo.Name}", pullRequestRef, parentBranchRef));
            
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
