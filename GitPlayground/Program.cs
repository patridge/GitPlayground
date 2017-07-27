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

            await DissectSubmoduleRepo(gitParentRepoPath, gitParentRepoBranchName, gitSubmoduleRepoPath, gitSubmoduleRepoBranchName);

            //await DoSomeGitHubStuff(gitHubPersonalAccessToken);
        }

        static async Task DissectSubmoduleRepo(string gitParentRepoPath, string gitParentRepoBranchName, string gitSubmoduleRepoPath, string gitSubmoduleRepoBranchName)
        {
            using (var repo = new LibGit2Sharp.Repository(gitSubmoduleRepoPath))
            {
                var branch = repo.Branches[gitSubmoduleRepoBranchName];
                var newestCommit = branch.Commits.First(); // may need sorting
                Console.WriteLine($"{newestCommit.Sha}: {newestCommit.Message}");
            }
        }

        static async Task DoSomeGitHubStuff(string accessToken)
        {
            var client = new GitHubClient(new ProductHeaderValue("submodule-helper"));
            var tokenAuth = new Octokit.Credentials(accessToken);
            client.Credentials = tokenAuth;

            // TODO: Handle auth failure.
            var user = await client.User.Current();

            Console.WriteLine($"{user.Name}");
        }

        static void ShowHelp(OptionSet os)
        {
            Console.WriteLine("GitPlayground [options]");
            os.WriteOptionDescriptions(Console.Out);
            Environment.Exit(1);
        }
    }
}
