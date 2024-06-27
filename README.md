WORK IN PROGRESS

Hand written TOML v.1.0.0 parser. Currently passes all tests in the official test suite for TOML v1.0.0.
It is pretty fast (as far as C# goes), but still has room for performance optimization.

The speed increase over something like Tomlyn only really shows in large files.

As an example, the provided benchmark file, the aptly named "gigatest2.toml" takes about 300ms worst case, and 250-270 average case (depends on a few things),
while in Tomlyn, using the default model, it took me 1000+ ms even in the best case (i5-11400F).

Note: the file was not made by me. Unfortunately, I cannot find the original source. If you know it, please let me know
so that I can attribute it properly.
