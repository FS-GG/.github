#ifndef FSGG_LEARN_CUSTODY_ROOT_H
#define FSGG_LEARN_CUSTODY_ROOT_H
#include "custody_adapter.h"
#define LR_VERSION 1u
#define LR_NORMAL_MAX 26u
#define LR_STORE_COUNT 4u
#define LR_REMOVAL_MAX 32u
#define LR_RECIPE_MAX 38u
#define LR_SCHEMA "fsgg.learn.root-command-program/1"
typedef enum { LR_C4=1,LR_INACTIVE=2 } lr_target;
typedef enum { LR_MAPPED_UID0_ENGINE=1 } lr_engine_profile;
typedef struct {
 cc_view executable,working_directory;
 const cc_view *arguments; size_t argument_count;
 const lc_environment *environment; size_t environment_count;
 uint32_t output_limit;
} lr_command;
typedef struct { cc_view step; lr_command command; } lr_step;
typedef struct {
 uint32_t version,size; cc_view schema,recipe_sha256,expected_input_sha256,custody_identity;
 lr_target target; cc_view work_root,evidence_root,bundle_nonce[2];
 const lr_step *steps; size_t step_count,normal_count;
 /* Root obtains these exact prefixes from the shared command producer. Only
  * the trailing ID is supplied by the adapter's own retired census result. */
 lr_command removal_prefix[LR_STORE_COUNT];
 uint32_t removal_limit,maximum_commands;
} lr_recipe;
typedef struct {
 const lr_recipe *recipe;
 uint32_t normal_cursor,store,phase,removed,total_removed,pending_kind,pending_store;
 uint32_t known_bundles,cleanup_started,aborting,pending,poisoned,terminal;
 uint32_t census_count;
 unsigned char census[LR_REMOVAL_MAX][64];
 const char *failure;
} lr_state;
typedef struct { const lr_command *command; cc_view removal_id; unsigned bundle; } lr_selection;
/* Pure finite policy. These APIs inspect no kernel state and authenticate no
 * admission, recipe source, directory, process result or engine profile. The
 * adapter feeds only its own captured/retired results; no such callback exists
 * on the request channel. Root retains independent recipe/source authority. */
int lr_validate(const lr_recipe *,const char **);
int lr_init(lr_state *,const lr_recipe *,const char **);
int lr_note_bundle(lr_state *,unsigned);
int lr_select(lr_state *,int cleanup,lr_selection *);
int lr_retired(lr_state *,int exit_code,cc_view output,int stdout_eof,int stderr_eof);
int lr_terminal(const lr_state *);
void lr_poison(lr_state *,const char *);
/* Parse the verified namespace's two singleton mappings, never a requester UID. */
int lr_mapped_uid0(const char *,uint32_t *);
#endif
