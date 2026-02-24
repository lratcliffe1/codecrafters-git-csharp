# C# Git

This repository contains my solution to the
["Build Your Own Git" challenge](https://app.codecrafters.io/courses/git/overview).

Implemented core Git plumbing including repository initialization, object hashing and parsing (blob,
tree, commit), refs/HEAD management, and cloning via packfile decoding (varints and deltas).

## What I have done

- Built command handling in [`src/GitCommandHandler.cs`](src/GitCommandHandler.cs) for:
  - `init`
  - `cat-file -p`
  - `hash-object -w`
  - `ls-tree (--name-only)`
  - `write-tree`
  - `commit-tree`
  - `clone`
- Implemented repository setup in [`src/Commands/InitHelper.cs`](src/Commands/InitHelper.cs):
  - creates `.git`, `objects`, and refs directories,
  - initializes `HEAD` to `refs/heads/main`.
- Added blob read/write plumbing in [`src/Commands/BlobHelper.cs`](src/Commands/BlobHelper.cs) and [`src/Services/GitObjectWriter.cs`](src/Services/GitObjectWriter.cs):
  - writes Git object headers,
  - computes SHA-1 object IDs,
  - compresses and stores objects in `.git/objects`.
- Implemented tree parsing/writing and checkout logic in [`src/Commands/TreeHelper.cs`](src/Commands/TreeHelper.cs):
  - serializes tree entries (`mode name\\0sha`),
  - parses binary tree objects for `ls-tree`,
  - recursively checks out tree/blob objects for clone.
- Implemented commit object construction in [`src/Commands/CommitHelper.cs`](src/Commands/CommitHelper.cs):
  - writes tree + parent + author/committer metadata,
  - stores commit objects with valid Git headers.
- Implemented refs/HEAD management in [`src/Services/GitRefHelper.cs`](src/Services/GitRefHelper.cs).
- Built HTTP clone flow in [`src/Commands/Clone/GitProtocolClient.cs`](src/Commands/Clone/GitProtocolClient.cs):
  - discovers remote refs (`info/refs`),
  - requests packfiles (`git-upload-pack`).
- Implemented packfile decoding in [`src/Commands/Clone/PackfileParser.cs`](src/Commands/Clone/PackfileParser.cs), [`src/Commands/Clone/PackfileInflater.cs`](src/Commands/Clone/PackfileInflater.cs), and [`src/Commands/Clone/DeltaResolver.cs`](src/Commands/Clone/DeltaResolver.cs):
  - parses pack headers/object headers,
  - decodes varint sizes and offset distances,
  - inflates standard/delta objects,
  - resolves `REF_DELTA` and `OFS_DELTA` chains.

## Architecture choices

- Used DI via `Microsoft.Extensions.DependencyInjection` in [`src/Program.cs`](src/Program.cs) to keep command and service wiring explicit.
- Added a repository abstraction in [`src/Services/Repository.cs`](src/Services/Repository.cs) so `.git` path resolution and root switching are centralized.
- Split object concerns into small helpers:
  - `SharedUtils` for compression/hash/header parsing,
  - `GitObjectWriter` for writing typed objects,
  - command helpers for blob/tree/commit behavior.
- Used an `ObjectStore` cache + disk fallback in [`src/Commands/Clone/ObjectStore.cs`](src/Commands/Clone/ObjectStore.cs) to support fast delta base lookups while keeping persisted objects canonical.
- Implemented pending-delta queues in pack parsing so out-of-order delta dependencies can be resolved after the first pass.

## What I have learnt

- Git object handling is mostly strict byte-format work: header framing and binary parsing are easy to get wrong without clear boundaries.
- Tree and commit objects are simple conceptually, but correctness depends on exact serialization order and formatting.
- Packfile parsing requires careful control of offsets and continuation bits, especially with varint decoding and delta instructions.
- Delta resolution is much easier to reason about with a staged approach: parse/store first, then retry unresolved dependencies.
- Separating repository path logic from command logic makes clone and local commands easier to maintain.

## Run locally

1. Ensure `.NET 9` is installed.
1. Run `./your_program.sh`.
