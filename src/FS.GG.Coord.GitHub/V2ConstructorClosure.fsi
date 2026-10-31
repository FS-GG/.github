namespace FS.GG.Coord.GitHub

/// The constructor's source closure (FS-GG/.github#5113): the repository paths whose content can change what the
/// enrollment constructor, its planner and the hosts that run it do. `route-preflight` `ConstructorIdentity` admits an
/// installed constructor against a later protected `main` only when that `main` descends from the constructor's
/// source revision and no path in this closure changed between them. It is the same idea as
/// `scripts/release_source_closure.py` (#5111), restricted to the compiled hosts.
///
/// The closure is fail-closed first and derived second:
///
/// * the root build configuration MSBuild reads (`global.json`, `Directory.*.props`/`.targets`, NuGet configuration,
///   `.gitattributes`, `.editorconfig`, `.config/` and the imported `dist/dotnet/`) is always inside;
/// * the rest is derived from the constructor's own source tree, never restated as a file list: every host project
///   (`roots`), each project directory as a whole, their transitive `ProjectReference`, linked item and `Import`
///   paths, and every ancestor `Directory.Build.*`/`Directory.Packages.props`, present or not;
/// * an ADDED file at the repository root, or in a directory that already holds an exact closure path, is a closure
///   change, so nothing can be shadowed in beside a closure file.
///
/// This module is itself inside the closure, as is every input of the derivation, so an unchanged closure derives the
/// same closure at both commits. Refusal is the safe failure: an MSBuild property path the derivation cannot resolve,
/// a path escaping the repository, a directory too broad to be a closure entry, or a change listing it cannot parse
/// is an `Error`, never an empty closure. Pure: the caller reads the tree.
module V2ConstructorClosure =

    /// Exact paths plus directory prefixes (each ending in '/').
    type Closure = { Paths: Set<string>; Prefixes: Set<string> }

    /// The host projects whose compiled closure the constructor runs in: the constructor assembly itself, the
    /// coordination engine and the `BoardV2Import` tool.
    val roots: string list

    /// The always-inside build configuration entries; a trailing '/' is a prefix.
    val buildRoot: string list

    /// Derive the closure at one commit from its tracked file set and a reader of a tracked file's text.
    val derive: files: Set<string> -> read: (string -> string option) -> Result<Closure, string>

    /// Whether a path is an exact closure path or lies under a closure prefix.
    val covers: closure: Closure -> path: string -> bool

    /// Whether an ADDED path could shadow a closure file: at the root, or beside an exact closure path.
    val shadows: closure: Closure -> path: string -> bool

    /// One entry of `git diff --no-renames --name-status -z`.
    type Change = { Status: char; Path: string }

    /// Parse `git diff --no-renames --name-status -z` output. Only A, D, M and T are admitted; anything else (a
    /// rename or copy pair, an unmerged entry, a truncated record) is an `Error`.
    val parseNameStatus: text: string -> Result<Change list, string>

    /// The changes that are inside the closure: a covered path of any status, or an added path that shadows.
    val changesInside: closure: Closure -> changes: Change list -> Change list
