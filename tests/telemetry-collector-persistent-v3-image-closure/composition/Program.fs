open System

match Environment.GetCommandLineArgs() |> Array.skip 1 with
| [||]->
    CallerCompositionTests.testIdentity()
    JoinedCallerTests.callbackRetention()
| [|"--joined-inert"|]->
    CallerCompositionTests.testIdentity()
    JoinedCallerTests.run()
| _->invalidArg "args" "expected no arguments or --joined-inert"
