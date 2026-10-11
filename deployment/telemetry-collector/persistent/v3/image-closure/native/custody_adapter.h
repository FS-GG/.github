#ifndef FSGG_LEARN_CUSTODY_ADAPTER_H
#define FSGG_LEARN_CUSTODY_ADAPTER_H
#include "custody_core.h"
#define LC_VERSION 1u
#define LC_SLOT_MAX 256u
#define LC_CAP_MAX 1032u
#define LC_STRING_MAX CC_FRAME_MAX
#define LC_VECTOR_MAX 4096u
#define LC_SCHEMA "fsgg.learn.custody-operation/1"
typedef enum { LC_PRODUCTION=1, LC_CONTROLLED_NATIVE=2 } lc_identity_class;
typedef enum { LC_OPERATION_CGROUP=1,LC_CONTROL_LEAF,LC_REQUESTER_LEAF,LC_COMMAND_CGROUP,
 LC_USER_NAMESPACE,LC_MOUNT_NAMESPACE,LC_EXECUTABLE,LC_SUPERVISOR,LC_DIRECTORY,LC_CHANNEL,LC_NAMESPACE_KEEPER } lc_role;
typedef struct {
 uint32_t version,size; lc_role role; uint32_t slot; int descriptor;
 uint64_t expected_file_system,device,inode,mount_identity; int access;
} lc_capability;
typedef struct { cc_view name,value; } lc_environment;
typedef struct {
 uint32_t version,size,slot; cc_view command_identity,executable;
 const cc_view *arguments; size_t argument_count;
 const lc_environment *environment; size_t environment_count;
 cc_view working_directory; uint32_t output_limit;
 uint32_t executable_handle,supervisor_handle,directory_handle,cgroup_handle;
} lc_command_slot;
typedef struct {
 uint32_t version,size; cc_view schema,operation_id,custody_identity,admission_sha256,expected_input_sha256;
 lc_identity_class identity_class;
 cc_view source_sha256,adapter_binary_sha256,toolchain_closure_sha256,native_profile_sha256;
 uint64_t deadline_utc_ms,admitted_utc_ms,admitted_boot_ns,deadline_boot_ns,cleanup_reserve_ns;
 uint64_t aggregate_output_bytes,memory_max_bytes,cpu_max_micros,cpu_period_micros;
 uint32_t max_owned_tasks,max_active_commands,maximum_commands;
 const lc_command_slot *command_slots; size_t command_count;
 const lc_capability *handles; size_t handle_count;
} lc_operation;
typedef struct {
 unsigned char operation_id[64],command_identity[64]; uint32_t slot;
 int retained_pidfd,command_cgroup_descriptor,leader_exit_known,leader_exit_code,leader_signal;
 int recursive_empty,stdout_eof,stderr_eof; uint64_t stdout_captured,stderr_captured;
 int live_admission,cleanup_settled;
} lc_observation;
typedef struct lc_adapter lc_adapter;
/* Pure shape checks never authenticate root admission or inspect host protection.
 * The existing admitted controller must bind actual caller/source/profile, sealed
 * exclusive control paths and immutable visible ELF/loader closures before open.
 * There is no descriptor decoder or authority inferred from these digest fields. */
int lc_validate(const lc_operation *,const char **failure);
/* Copies all bounded values and duplicates retained capabilities before serving.
 * Caller keeps its own duplicates for terminal recovery under original identity.
 * Single controller owner, outside workload namespaces, with no sibling threads
 * doing wait/reaping or modifying descriptors. No operations on request-supplied FDs. */
lc_adapter *lc_open(const lc_operation *,cc_workspace,cc_lease *,size_t,const char **failure);
/* Continuous idle-client capture and shared accounting; only actual kernel
 * observations project to cc_result. No external accepted/retired callback. */
int lc_serve(lc_adapter *);
const char *lc_failure(const lc_adapter *);
int lc_observe_original(const lc_adapter *,uint32_t,lc_observation *);
/* Refuses to release retained handles while a command has uncertain retirement.
 * Root must preserve this object/its original capabilities on return0. */
int lc_release(lc_adapter *);
#endif
