namespace FS.GG.Coord.Cli

module WorkspaceTelemetryApplication =
    type Binding
    val resolveBinding: configArg: string option -> repositoryArg: string option -> Result<Binding,string list>
    val tryPublishBinding: binding: Binding -> payload: byte array -> Result<string,string list>
    val tryDrainBinding: binding: Binding -> Result<string,string list>
    val tryLocalStoreRootBound: binding: Binding -> Result<string option,string list>
    val tryPublish: configArg: string option -> repositoryArg: string option -> payload: byte array -> Result<string,string list>
    val tryPublishExpected: configArg: string option -> repositoryArg: string option -> expectedProducer: string option -> payload: byte array -> Result<string,string list>
    val tryDrain: configArg: string option -> repositoryArg: string option -> Result<string,string list>
    val tryDrainExpected: configArg: string option -> repositoryArg: string option -> expectedBinding: string option -> Result<string,string list>
    val privateStateRoot: configArg: string option -> repositoryArg: string option -> Result<string,string list>
    val tryLocalStoreRoot: configArg: string option -> repositoryArg: string option -> Result<string option,string list>
    val isConfigured: configArg: string option -> bool
    val tryBinding: configArg: string option -> repositoryArg: string option -> (string * string * string) option
    val configuredPath: configArg: string option -> string
    val selectedProducer: configArg: string option -> repositoryArg: string option -> string option
    val tryPublishBound: configArg: string option -> repositoryArg: string option -> expectedProducer: string option -> expectedBinding: string option -> payload: byte array -> Result<string,string list>
    val run: action: string -> args: string list -> int
