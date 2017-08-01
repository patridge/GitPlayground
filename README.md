## What's it do?

This is a command-line utility to help update submodule references in GitHub repositories via pull requests.

Here's what it does at a high level

1. Checks what version the submodule repo is current at (for a given branch, defaulting to **master**)
2. Checks what hash/SHA on the submodule repo the parent repo currently has the submodule pointing to (for a given branch, defaulting to **master**)
3. If those versions differ via string check (not any sort of "newness" check)
    1. Ensure the pull request creator has a fork of the parent repo, creating if necessary ([fork creation currently broken](https://github.com/patridge/SubmoduleUpdateGenerator/issues/1))
	2. Create a `patch-#` branch on that fork to contain the submodule update changes
	3. Updates the submodule target SHA to the latest on the submodule repo
	4. Pushes changes to fork repo on GitHub
	5. Creates a pull request on the parent repo's branch, compared to the fork repo's patch branch

## How did we get here?

This started as an experiment in learning Octokit to help out with a separate project during [Microsoft's One Week Hackathon](https://blogs.microsoft.com/firehose/2017/07/24/microsofts-one-week-hackathon-kicks-off-this-year-with-nonprofits-hacking-alongside-employees/).

## Feature Creep

- [ ] Allow PR from branch in parent repo rather than fork (e.g., you are not owner of parent, but have permission to branch)

## Sample command line params

These are likely not useful without customization.

### Some test repos

-owner=patridge -parent=GitPlayground-SampleParent -parentBranch=master -subOwner=patridge -sub=GitPlayground-SampleSubmodule -subBranch=master -pullOwner=your-username

### mono/monodevelop vs. mono/mono-tools submodule

-owner=mono -parent=monodevelop -parentBranch=master -subOwner=mono -sub=mono-tools -subBranch=master -pullOwner=patridge -dryrun=true
