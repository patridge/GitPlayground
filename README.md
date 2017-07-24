## TODO

1. list repo X's submodules
  1. sub repo path
  2. sub repo branch
  3. sub repo branch's hash
2. get submodule repo latest on branch X
  1. sub repo branch's current hash
  2. commits since submodule reference hash
3. bump submodule locally
  1. branch local
    1. [?assume?] require no uncommitted
    2. [?assume?] submodule path is as up-to-date as desired
  2. commit to bump
    `git submodule update --remote local/path/to/submodule`
3. create PR to bump submodule
  1. push local branch to own remote
  2. create PR against original remote