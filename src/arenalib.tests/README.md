# arenalib tests

`selftest/` is one bflat program with ~200 assertions. It prints a failure line per broken
assertion and a `PASS`/`FAIL` summary, and exits non-zero if anything failed.

```
bflat build --stdlib:arena -o arenatests            # from selftest/
bflat build --stdlib:arena --arena-checks -o arenatests-checked
```

Run it both ways: the release build proves the checks cost nothing, and the checked build
proves they do not break anything.

`negative/<name>/` are one-program-per-directory tests that are *supposed* to abort. Each is
a lifetime bug the checked build must catch; most of them run to completion in the release
build, which is the point. They live in separate directories because bflat globs `*.cs`
recursively when no files are named, and every one of them has its own `Main`.
