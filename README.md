## TODO

1. find submodule in parent repo
    1. pull .gitmodules file
	2. extract submodule path for given submodule repo
	3. determine parent's current submodule hash target
2. check submodule repo for changes
    1. ~get given submodule repo's latest hash for given branch~
	2. verify difference between 1.3 value and 2.1 value
3. set up a Pull Request
    1. if 2.2, create a branch for PR
	2. commit submodule hash update to PR branch
	3. update PR branch
4. create PR to bump submodule
