using Mono.Options;
using Nito.AsyncEx;
using Octokit;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Threading.Tasks;

namespace SubmoduleUpdateGenerator
{
    class Program
    {
        static void Main(string[] args)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            AsyncContext.Run(() => MainAsync(args));
            stopwatch.Stop();
            Console.WriteLine($"Elapsed time: {stopwatch.ElapsedMilliseconds / 1000.0}s");
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
            string gitPullRequestTitle = null;
            bool gitDryRun = false;

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
                { "pullMessage=", "Title of the generated pull request", pullMessage => gitPullRequestTitle = pullMessage },
                { "dryrun=", "Dry run: do not make final pull request on parent GitHub repo", dryRun => { if (!bool.TryParse(dryRun, out gitDryRun)) { gitDryRun = false; } } },
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

            await UpdateSubmoduleTarget(gitHubClient, gitPullRequestOwner, gitPullRequestTitle, parentRepoInfo, submoduleRepoInfo, gitDryRun);
        }

        static string CreateDefaultPullRequestTitle(string submoduleOwnerName, string submoduleRepoName, string submoduleCommitHash)
        {
            return $"Bump to {submoduleOwnerName}/{submoduleRepoName}@{submoduleCommitHash}";
        }

        static async Task UpdateSubmoduleTarget(GitHubClient gitHubClient, string pullRequestOwnerName, string pullRequestTitle, RepoQueryInfo parentRepoInfo, RepoQueryInfo submoduleRepoInfo, bool gitDryRun)
        {
            var apiConnection = new ApiConnection(gitHubClient.Connection);
            var repoClient = new RepositoriesClient(apiConnection);
            var forksClient = new RepositoryForksClient(apiConnection);
            var referencesClient = new ReferencesClient(apiConnection);
            var pullRequestsClient = new PullRequestsClient(apiConnection);
            var commitsClient = new CommitsClient(apiConnection);

            // Get parent repo details.
            (var parentRepoId, var parentBranchLatestSha) = await GetRepoIdAndLatestCommitHash(gitHubClient, parentRepoInfo.Owner, parentRepoInfo.Name, parentRepoInfo.BranchName);
            Console.WriteLine($"Parent repo branch latest hash: {parentBranchLatestSha}.");

            // Get latest submodule repo details
            (var submoduleRepoId, var submoduleBranchLatestSha) = await GetRepoIdAndLatestCommitHash(gitHubClient, submoduleRepoInfo.Owner, submoduleRepoInfo.Name, submoduleRepoInfo.BranchName);
            Console.WriteLine($"Submodule repo branch latest hash: {submoduleBranchLatestSha}.");

            // Find submodule path in parent .gitmodules file.
            var gitmodulesContent = await repoClient.Content.GetAllContents(parentRepoId, ".gitmodules");
            if (gitmodulesContent.Count != 1) { throw new InvalidOperationException("Did not find a .gitmodules file in the parent repo."); }
            // ASSUMPTION: submodule path ends with submodule repo name.
            var gitmodulesGroupForSubmodule = FindGitSubmoduleEntry(gitmodulesContent[0].Content, submoduleRepoInfo.Name);
            if (gitmodulesGroupForSubmodule == null) { throw new InvalidOperationException("Did not find requested submodule repo in parent repo's .gitmodules file."); }
            var submodulePath = gitmodulesGroupForSubmodule.Path;
            Console.WriteLine($"Found submodule path: {submodulePath}");

            // NOTE: To get submodule target hashes, you have to query the parent directory.
            // Get the path one level up from submodule path.
            var submodulePathParts = submodulePath.Split(new[] { '/' });
            var submodulePathImmediateParentPath = string.Join("/", submodulePathParts.Take(submodulePathParts.Length - 1));
            var submodulePathContents = await repoClient.Content.GetAllContents(parentRepoId, submodulePathImmediateParentPath);
            // Find our submodule's "file" in the parent directory's contents.
            var submoduleContent = submodulePathContents.FirstOrDefault(content => content.Path == submodulePath);
            if (submoduleContent == null) { throw new NotImplementedException("Could not retrieve submodule content from repo to get target SHA."); }
            // That submodule "file" has a hash that corresponds with the submodule target.
            var parentSubmoduleTargetSha = submoduleContent.Sha;
            Console.WriteLine($"Parent current submodule hash: {parentSubmoduleTargetSha}.");
            if (parentSubmoduleTargetSha == submoduleBranchLatestSha)
            {
                Console.WriteLine($"Parent submodule target hash matches latest on submodule repo branch: {parentSubmoduleTargetSha}.");
                Console.WriteLine($"No pull request is needed.");
                return;
            }

            var pullRequestRepoName = await GetOrCreateUserForkRepoName(repoClient, forksClient, pullRequestOwnerName, parentRepoInfo, parentRepoId);
            (long pullRequestOwnerForkRepoId, RepoQueryInfo pullRequestRepoInfo) = await GetOrCreateUserForkBranch(gitHubClient, repoClient, referencesClient, pullRequestOwnerName, pullRequestRepoName, parentBranchLatestSha);
            var pullRequestBranchName = pullRequestRepoInfo.BranchName;

            // Create commit on parent to update submodule hash target.
            string pullRequestBranchRef = await CreateSubmoduleUpdateCommit(gitHubClient, pullRequestOwnerName, parentRepoInfo.Name, submoduleRepoInfo.Owner, submoduleRepoInfo.Name, parentBranchLatestSha, submoduleBranchLatestSha, submodulePath, pullRequestOwnerForkRepoId, pullRequestBranchName);

            var parentBranchRef = $"heads/{parentRepoInfo.BranchName}";
            var pullRequestSourceRef = $"{pullRequestBranchRef}";
            var isPullRequestOwnerSameAsParentRepoOwner = pullRequestOwnerName == parentRepoInfo.Owner;
            if (!isPullRequestOwnerSameAsParentRepoOwner)
            {
                // For a PR, the comparison ref to a different user requires a prefix.
                pullRequestSourceRef = $"{pullRequestOwnerName}:{pullRequestBranchRef}";
            }

            Console.WriteLine($"Creating pull request from {pullRequestSourceRef} to {parentRepoInfo.Owner}:{parentBranchRef}.");
            // Create a pull request from {pullRequestOwner}/{pullRequestRepoName} to {parentRepoInfo.Owner}/{parentRepoInfo.Name}
            try
            {
                if (!gitDryRun)
                {
                    pullRequestTitle = pullRequestTitle ?? CreateDefaultPullRequestTitle(submoduleRepoInfo.Owner, submoduleRepoInfo.Name, submoduleBranchLatestSha);
                    var newPullRequest = await pullRequestsClient.Create(parentRepoId, new NewPullRequest(pullRequestTitle, pullRequestSourceRef, parentBranchRef));
                    Console.WriteLine($"Pull request created: {newPullRequest.HtmlUrl}.");
                }
                else
                {
                    Console.WriteLine($"[Dry run!] Would have created pull request on parent repo.");
                }
            }
            catch (Exception)
            {
                Console.WriteLine($"Failed to create pull request.");
            }
        }

        private static async Task<string> CreateSubmoduleUpdateCommit(GitHubClient gitHubClient, string pullRequestOwnerName, string parentRepoName, string submoduleOwnerName, string submoduleRepoName, string parentBranchLatestSha, string submoduleBranchLatestSha, string submodulePath, long pullRequestOwnerForkRepoId, string pullRequestBranchName)
        {
            var updateParentTree = new NewTree { BaseTree = parentBranchLatestSha };
            updateParentTree.Tree.Add(new NewTreeItem
            {
                Mode = FileMode.Submodule,
                Sha = submoduleBranchLatestSha,
                Path = submodulePath,
                Type = TreeType.Commit,
            });
            var newParentTree = await gitHubClient.Git.Tree.Create(pullRequestOwnerForkRepoId, updateParentTree);
            var commitMessage = $"Update submodule {submoduleOwnerName}/{submoduleRepoName} to latest.";
            var newCommit = new NewCommit(commitMessage, newParentTree.Sha, parentBranchLatestSha);
            var pullRequestBranchRef = $"heads/{pullRequestBranchName}";
            var commit = await gitHubClient.Git.Commit.Create(pullRequestOwnerName, parentRepoName, newCommit);
            Console.WriteLine($"Updating submodule with commit on fork branch: {commit.Sha}.");
            await gitHubClient.Git.Reference.Update(pullRequestOwnerForkRepoId, pullRequestBranchRef, new ReferenceUpdate(commit.Sha));
            return pullRequestBranchRef;
        }

        static async Task<(long repoId, string latestCommitHash)> GetRepoIdAndLatestCommitHash(GitHubClient gitHubClient, string repoOwner, string repoName, string repoBranchName)
        {
            var repoId = (await gitHubClient.Repository.Get(repoOwner, repoName)).Id;
            var latestCommitHash = (await gitHubClient.Git.Tree.Get(repoId, repoBranchName)).Sha;
            return (repoId, latestCommitHash);
        }

        static async Task<(long repoId, RepoQueryInfo repoInfo)> GetOrCreateUserForkBranch(GitHubClient gitHubClient, RepositoriesClient repoClient, ReferencesClient referencesClient, string pullRequestOwnerName, string pullRequestRepoName, string parentBranchLatestSha)
        {
            var pullRequestBranchPrefix = "patch-";
            var pullRequestOwnerForkRepoId = (await gitHubClient.Repository.Get(pullRequestOwnerName, pullRequestRepoName)).Id;
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
            var branchReference = $"refs/heads/{pullRequestBranchName}";
            Console.WriteLine($"Creating PR fork branch: {pullRequestBranchName}.");
            var pullRequestBranch = await referencesClient.Create(pullRequestOwnerForkRepoId, new NewReference(branchReference, parentBranchLatestSha));
            return (pullRequestOwnerForkRepoId, new RepoQueryInfo { BranchName = pullRequestBranchName, Name = pullRequestRepoName, Owner = pullRequestOwnerName });
        }

        static GitSubmoduleEntry FindGitSubmoduleEntry(string gitmodulesFileContent, string submoduleRepoName)
        {
            var gitmodulesGroupForSubmodule = gitmodulesFileContent
                // Split by line breaks, removing empties for doubles on `\r\n`
                .Split(new[] { '\n', '\r', }, StringSplitOptions.RemoveEmptyEntries)
                // Break into groups of lines per submodule chunk.
                .Aggregate(
                    new List<List<string>>(),
                    (acc, line) =>
                    {
                        if (line.StartsWith("["))
                        {
                            acc.Add(new List<string>());
                        }
                        acc.Last().Add(line);
                        return acc;
                    }
                )
                .Select(submoduleLines =>
                {
                    // Parse set of lines into submodule data.
                    var submoduleEntry = new GitSubmoduleEntry();
                    foreach (var line in submoduleLines)
                    {
                        if (line.StartsWith("[submodule"))
                        {
                            submoduleEntry.Name = new string(line
                                .SkipWhile(c => c != '"')
                                .Skip(1)
                                .TakeWhile(c => c != '"').ToArray());
                        }
                        else if (line.TrimStart().StartsWith("path"))
                        {
                            submoduleEntry.Path = new string(line
                                .SkipWhile(c => c != '=')
                                .Skip(2)
                                .TakeWhile(c => c != '\r' && c != '\n').ToArray());
                        }
                        else if (line.TrimStart().StartsWith("url"))
                        {
                            submoduleEntry.Url = new string(line
                                .SkipWhile(c => c != '=')
                                .Skip(2)
                                .TakeWhile(c => c != '\r' && c != '\n').ToArray());
                        }
                    }
                    return submoduleEntry;
                })
                // ASSUMPTION: URL for submodule will end with the submodule repo name + ".git"
                .FirstOrDefault(submoduleEntry => submoduleEntry.Url.EndsWith($"{submoduleRepoName}.git", StringComparison.InvariantCultureIgnoreCase));
            return gitmodulesGroupForSubmodule;
        }

        static async Task<string> GetOrCreateUserForkRepoName(RepositoriesClient repoClient, RepositoryForksClient forksClient, string pullRequestOwnerName, RepoQueryInfo parentRepoInfo, long parentRepoId)
        {
            var pullRequestForkRepoName = parentRepoInfo.Name;
            var parentForks = await forksClient.GetAll(parentRepoId);
            var isPullRequestOwnerSameAsParentRepoOwner = pullRequestOwnerName == parentRepoInfo.Owner;
            if (!isPullRequestOwnerSameAsParentRepoOwner)
            {
                // Deal with user's parent repo fork.
                var pullRequestOwnerFork = parentForks.FirstOrDefault(f => f.Owner.Login == pullRequestOwnerName);
                if (pullRequestOwnerFork != null)
                {
                    // Update PR repo name with PR-owner's fork name (in case it has changed).
                    pullRequestForkRepoName = pullRequestOwnerFork.Name;
                    // NOTE: It doesn't appear that we need to update an outdated fork to branch from the upstream commit hash.
                }
                else
                {
                    // If PR owner doesn't have fork of repo, make one.
                    // NOTE: This API call can take a few seconds to return from GitHub.
                    var pullRequestOwnerReposWithNamesLikeParent = (await repoClient.GetAllForCurrent())
                        .Where(repo => repo.Owner.Name == pullRequestOwnerName && repo.Name.StartsWith(parentRepoInfo.Name));
                    var hasRepoWithParentNameThatIsNotFork = pullRequestOwnerReposWithNamesLikeParent.Any(repo => repo.Name == parentRepoInfo.Name);
                    string pullRequestForkName = parentRepoInfo.Name;
                    if (hasRepoWithParentNameThatIsNotFork)
                    {
                        // Handle if user has non-fork repo of the same name as the parent repo.
                        var repoPrefix = $"{parentRepoInfo.Name}-";
                        var largestRepoSuffix = pullRequestOwnerReposWithNamesLikeParent
                            .Where(repo => repo.Name.StartsWith(repoPrefix))
                            .Select(b =>
                            {
                                var repoSuffix = new string(b.Name.Skip(repoPrefix.Length).ToArray());
                                bool repoSuffixNumeric = int.TryParse(repoSuffix, out int repoSuffixNumber);
                                return (repoSuffixNumeric ? (int?)repoSuffixNumber : null);
                            })
                            .Where(repoSuffixNumber => repoSuffixNumber != null)
                            .OrderByDescending(n => n)
                            .FirstOrDefault() ?? 0;
                        pullRequestForkName = $"{repoPrefix}{largestRepoSuffix + 1}";
                    }
                    Console.WriteLine($"Creating PR fork repo: {pullRequestOwnerName}/{pullRequestForkName}");
                    throw new NotImplementedException("Unable to create a fork automatically right now. Create it manually and try again. (It can also take up to 5 minutes to be accessible.)");
                    // NOTE: If you create a fork on an owner who already has one, you just get back the fork they already had.
                    // TODO: For some reason this fails with a 404 error.
                    //pullRequestOwnerFork = await forksClient.Create(pullRequestOwner, pullRequestForkName, new NewRepositoryFork() { Organization = pullRequestOwner });
                    pullRequestForkRepoName = pullRequestForkName;
                }
            }
            else
            {
                Console.WriteLine($"No PR fork needed: parent repo owner matches PR owner; just branch and PR in parent repo.");
            }
            return pullRequestForkRepoName;
        }

        static void ShowHelp(OptionSet os)
        {
            Console.WriteLine("SubmoduleUpdateGenerator [options]");
            os.WriteOptionDescriptions(Console.Out);
            Environment.Exit(1);
        }
    }
}
