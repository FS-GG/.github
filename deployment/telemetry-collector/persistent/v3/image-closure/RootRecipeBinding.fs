namespace FSGG.Telemetry.PersistentV3.ImageClosure

open System
open System.IO

// Typed data corresponding to custody_root.h, never a resource/admission decoder.
// The independently admitted root controller still owns all capability binding.
module internal RootRecipeBinding=
    [<Literal>]
    let Schema="fsgg.learn.root-command-program/1"
    type Recipe={Version:uint32;Schema:string;RecipeSha256:string;ExpectedInputSha256:string;CustodyIdentity:string;Target:uint32;
                 WorkRoot:string;EvidenceRoot:string;NonceA:string;NonceB:string;Steps:RecipeStep array;NormalCount:int;
                 RemovalPrefixes:ProcessCommand array;RemovalLimit:int;MaximumCommands:int}
    let private require condition reason=if not condition then raise(InvalidDataException reason)
    let private copyCommand (command:ProcessCommand)={command with Arguments=Array.copy command.Arguments}
    let prepare (inputs:RootlessInputs)=
        let program=inputs.Program
        CustodyProtocol.sha inputs.ExpectedInput
        CommandRecipe.validate inputs.Selection inputs.Trusted inputs.Prepared inputs.CustodyIdentity program.Selection.Target inputs.ParentOci inputs.WorkRoot inputs.EvidenceRoot program
        let normal=match program.Selection.Target with "run-c4"->20|"run-inactive"->26|_->raise(InvalidDataException "native-recipe-target")
        require(program.NormalCommands=normal&&program.Steps.Length=normal+12&&program.MaximumCommands=normal+12+program.Selection.CleanupRemovalLimit) "native-recipe-counts"
        let exemplar=String('0',64)
        let prefixes=[|
            for logical in ["a";"b"] do
                for reload in [false;true] do
                    let command=CommandRecipe.removal program logical reload exemplar
                    require(command.Arguments.Length>0&&command.Arguments[command.Arguments.Length-1]=exemplar) "native-recipe-removal-operand"
                    yield {command with Arguments=command.Arguments[..command.Arguments.Length-2]}|]
        {Version=1u;Schema=Schema;RecipeSha256=CommandRecipe.identity program;ExpectedInputSha256=inputs.ExpectedInput;CustodyIdentity=inputs.CustodyIdentity;
         Target=(if program.Selection.Target="run-c4" then 1u else 2u);WorkRoot=inputs.WorkRoot;EvidenceRoot=inputs.EvidenceRoot;
         NonceA=program.Selection.NonceA;NonceB=program.Selection.NonceB;Steps=program.Steps|>Array.map(fun step->{step with Command=copyCommand step.Command});
         NormalCount=normal;RemovalPrefixes=prefixes;RemovalLimit=program.Selection.CleanupRemovalLimit;MaximumCommands=program.MaximumCommands}
