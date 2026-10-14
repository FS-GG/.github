open System

// No production resources, kernel callbacks or native library are opened by
// this entry. The full joined source fixture is added only in its admitted scope.
if Environment.GetCommandLineArgs().Length<>1 then invalidArg "args" "caller source identity controls take no arguments"
CallerCompositionTests.testIdentity()
