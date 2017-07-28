## TODO

1. find submodule in parent repo
    1. [x] pull .gitmodules file
	2. [x] extract submodule path for given submodule repo
	3. [x] determine parent's current submodule hash target
2. check submodule repo for changes
    1. [x] get given submodule repo's latest hash for given branch
	2. [x] verify difference between 1.3 value and 2.1 value
3. if 2.2, set up a Pull Request
    1. [x] prep PR creator fork
	    1. [x] fork to PR creator, if doesn't exist
		2. ~~get upstream into fork, if fork already exists~~
    1. [x] create a "patch-#" branch for PR
	2. [x] commit submodule hash update to PR branch
	3. [x] update PR branch on GitHub

## Feature Creep

- [ ] Allow PR from branch in parent repo rather than fork (e.g., you are not owner of parent, but have permission to branch)

## Sample command line params

These are likely not useful without customization.

### Some test repos

-owner=patridge -parent=GitPlayground-SampleParent -parentBranch=master -subOwner=patridge -sub=GitPlayground-SampleSubmodule -subBranch=master

### mono/monodevelop vs. mono/mono-tools submodule

-owner=mono -parent=monodevelop -parentBranch=master -subOwner=mono -sub=mono-tools -subBranch=master -pullOwner=patridge