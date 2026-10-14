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

    // This representation contains command data only. It supplies no kernel
    // capability, controller admission, mapped identity or process result.
    // The scope keeps every unmanaged view alive until the root-side consumer
    // has copied it; no pointer can escape through the return value.
    [<Struct; Runtime.InteropServices.StructLayout(Runtime.InteropServices.LayoutKind.Sequential)>]
    type internal NativeView={Pointer:nativeint;Length:unativeint}
    [<Struct; Runtime.InteropServices.StructLayout(Runtime.InteropServices.LayoutKind.Sequential)>]
    type internal NativeEnvironment={Name:NativeView;Value:NativeView}
    [<Struct; Runtime.InteropServices.StructLayout(Runtime.InteropServices.LayoutKind.Sequential)>]
    type internal NativeCommand={Executable:NativeView;WorkingDirectory:NativeView;Arguments:nativeint;ArgumentCount:unativeint;Environment:nativeint;EnvironmentCount:unativeint;OutputLimit:uint32}
    [<Struct; Runtime.InteropServices.StructLayout(Runtime.InteropServices.LayoutKind.Sequential)>]
    type internal NativeStep={Step:NativeView;Command:NativeCommand}
    [<Struct; Runtime.InteropServices.StructLayout(Runtime.InteropServices.LayoutKind.Sequential)>]
    type internal NativeRecipe={Version:uint32;Size:uint32;Schema:NativeView;RecipeSha256:NativeView;ExpectedInputSha256:NativeView;CustodyIdentity:NativeView;Target:uint32;
                               WorkRoot:NativeView;EvidenceRoot:NativeView;NonceA:NativeView;NonceB:NativeView;Steps:nativeint;StepCount:unativeint;NormalCount:unativeint;
                               Removal0:NativeCommand;Removal1:NativeCommand;Removal2:NativeCommand;Removal3:NativeCommand;RemovalLimit:uint32;MaximumCommands:uint32}
    let internal withNative (recipe:Recipe) (action:nativeint->unit)=
        require(OperatingSystem.IsLinux()&&Runtime.InteropServices.RuntimeInformation.ProcessArchitecture=Runtime.InteropServices.Architecture.X64&&IntPtr.Size=8) "native-recipe-linux-x64-required"
        require(recipe.Version=1u&&recipe.Schema=Schema&&(recipe.Target=1u||recipe.Target=2u)&&recipe.Steps.Length<=38&&recipe.NormalCount>0&&recipe.Steps.Length=recipe.NormalCount+12&&recipe.RemovalPrefixes.Length=4&&recipe.RemovalLimit>=0&&recipe.RemovalLimit<=32&&recipe.MaximumCommands=recipe.Steps.Length+recipe.RemovalLimit&&recipe.MaximumCommands<=70) "native-recipe-conversion-shape"
        let allocations=Collections.Generic.List<nativeint>()
        let mutable reserved=0L
        let allocate bytes=
            require(bytes>0&&int64 bytes<=16L*1024L*1024L-reserved) "native-recipe-conversion-memory-bound"
            let pointer=Runtime.InteropServices.Marshal.AllocHGlobal bytes
            allocations.Add pointer
            reserved<-reserved+int64 bytes
            pointer
        let view (value:string)=
            let bytes=Text.UTF8Encoding(false,true).GetBytes value
            require(bytes.Length<=32768) "native-recipe-conversion-string-bound"
            let pointer=allocate(bytes.Length+1)
            Runtime.InteropServices.Marshal.Copy(bytes,0,pointer,bytes.Length)
            Runtime.InteropServices.Marshal.WriteByte(pointer,bytes.Length,0uy)
            {Pointer=pointer;Length=unativeint bytes.Length}
        let array (rows:'T array)=
            if rows.Length=0 then 0n else
                let size=Runtime.InteropServices.Marshal.SizeOf typeof<'T>
                require(size>0&&rows.Length<=Int32.MaxValue/size) "native-recipe-conversion-array-bound"
                let pointer=allocate(size*rows.Length)
                for i in 0..rows.Length-1 do Runtime.InteropServices.Marshal.StructureToPtr(box rows[i],pointer+nativeint(i*size),false)
                pointer
        let command (value:ProcessCommand):NativeCommand=
            require(value.Arguments.Length<=256&&value.Environment.Count<=128&&value.OutputLimit=1024*1024) "native-recipe-conversion-command-bound"
            let arguments=value.Arguments|>Array.map view
            let environment=value.Environment|>Map.toArray|>Array.map(fun(name,value)->{Name=view name;Value=view value}:NativeEnvironment)
            {Executable=view value.Executable;WorkingDirectory=view value.WorkingDirectory;Arguments=array arguments;ArgumentCount=unativeint arguments.Length;
             Environment=array environment;EnvironmentCount=unativeint environment.Length;OutputLimit=uint32 value.OutputLimit}
        try
            let steps=recipe.Steps|>Array.map(fun value->{Step=view value.Step;Command=command value.Command}:NativeStep)
            let removals=recipe.RemovalPrefixes|>Array.map command
            let native={Version=recipe.Version;Size=uint32(Runtime.InteropServices.Marshal.SizeOf<NativeRecipe>());Schema=view recipe.Schema;RecipeSha256=view recipe.RecipeSha256;ExpectedInputSha256=view recipe.ExpectedInputSha256;CustodyIdentity=view recipe.CustodyIdentity;Target=recipe.Target;
                        WorkRoot=view recipe.WorkRoot;EvidenceRoot=view recipe.EvidenceRoot;NonceA=view recipe.NonceA;NonceB=view recipe.NonceB;Steps=array steps;StepCount=unativeint steps.Length;NormalCount=unativeint recipe.NormalCount;
                        Removal0=removals[0];Removal1=removals[1];Removal2=removals[2];Removal3=removals[3];RemovalLimit=uint32 recipe.RemovalLimit;MaximumCommands=uint32 recipe.MaximumCommands}:NativeRecipe
            let pointer=allocate(Runtime.InteropServices.Marshal.SizeOf<NativeRecipe>())
            Runtime.InteropServices.Marshal.StructureToPtr(native,pointer,false)
            action pointer
        finally
            for pointer in allocations do Runtime.InteropServices.Marshal.FreeHGlobal pointer
