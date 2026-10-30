#!/bin/sh
# C6 dotnet wrapper: every `dotnet` on PATH goes through 10 CLR slots (11th refuses after 10 s); build servers off.
export DOTNET_CLI_USE_MSBUILD_SERVER=0 UseSharedCompilation=false
exec /usr/local/libexec/learn/slot clr 10 10 -- /usr/local/libexec/learn/real-dotnet "$@"
