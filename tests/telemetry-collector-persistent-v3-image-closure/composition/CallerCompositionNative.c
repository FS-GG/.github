#define FSGG_CALLER_SOURCE_TEST 1
#include "../../../deployment/telemetry-collector/persistent/v3/image-closure/native/custody_adapter.c"
#include <sys/eventfd.h>
#include <sys/mman.h>
#include <stdatomic.h>
#include <limits.h>
/* Distinct test-only ELF. This factory creates an inert kernel boundary, never
 * lc_operation authority or an authentic provisioner. No child is created and
 * no namespace/cgroup is entered. Real private directory FDs, selected recipe,
 * core/parser/perform/capture/pump/cleanup and resource accounting are retained. */
typedef int (*source_effect)(uint32_t,const lr_command *,int *,const unsigned char **,size_t *,const unsigned char **,size_t *);
struct source_fixture {
 lc_adapter *adapter;
 source_effect effect;
 unsigned char *frame,*text;
 cc_node *nodes;
 cc_lease leases[70];
 char names[2][PATH_MAX],parents[2][PATH_MAX],lease_ids[70][65];
 int leader_fd[70],leader_exit[70],leader_ready[70];
 uint32_t births,signals,limits_checks,namespace_checks;
 _Atomic int serving;
};
static struct source_fixture *source_current;
static int source_namespace_join(lc_adapter *a,int visible) {
 struct source_fixture *f=source_current;if(!f||f->adapter!=a)return 0;
 f->namespace_checks++;
 if(!verify_root_parent(&a->root_binding.work_parent,a->root_uid,a->root_gid)||!verify_root_parent(&a->root_binding.evidence_parent,a->root_uid,a->root_gid))return 0;
 int proc=open("/proc/self",O_RDONLY|O_DIRECTORY|O_CLOEXEC);
 if(proc<0)return 0;
 int ok=root_parent_visible(a,proc);close(proc);
 if(visible)for(unsigned i=0;i<a->operation.command_count;i++) {
  if(!slot_requires_visibility(a,i))continue;
  const lc_command_slot *slot=&a->slots[i];
  int fd=open((const char *)slot->working_directory.data,O_RDONLY|O_DIRECTORY|O_CLOEXEC|O_NOFOLLOW);
  ok=ok&&fd>=0&&root_same_directory(fd,a->caps[slot->directory_handle].descriptor,a->root_uid,a->root_gid);
  if(fd>=0)close(fd);
 }
 return ok;
}
static int source_population(int fd,int *value) {
 struct source_fixture *f=source_current;if(!f)return -1;
 for(unsigned i=0;i<f->adapter->operation.command_count;i++)if(f->adapter->caps[i].descriptor==fd){*value=0;return 1;}
 return 0;
}
static int source_limits_unchanged(lc_adapter *a) {
 if(!source_current||source_current->adapter!=a)return 0;
 source_current->limits_checks++;
 /* These are finite test resource facts, not a delegated cgroup receipt. */
 return a->operation.memory_max_bytes==536870912&&a->operation.max_active_commands==1&&a->operation.maximum_commands<=70;
}
static int source_memfd(const unsigned char *bytes,size_t count) {
 if(count>CC_OUTPUT_MAX+1u||(count&&!bytes))return -1;
 int fd=memfd_create("learn-inert-command-output",MFD_CLOEXEC);if(fd<0)return -1;
 size_t written=0;
 while(written<count){ssize_t n=write(fd,bytes+written,count-written);if(n<=0){close(fd);return -1;}written+=(size_t)n;}
 if(lseek(fd,0,SEEK_SET)<0){close(fd);return -1;}return fd;
}
static long source_birth(lc_adapter *a,uint32_t slot,int out[2],int err[2],int handshake[2],int *pidfd) {
 struct source_fixture *f=source_current;
 if(!f||f->adapter!=a||slot>=70||!f->effect){errno=EINVAL;return -1;}
 int exit_code=0;const unsigned char *stdout_bytes=NULL,*stderr_bytes=NULL;size_t stdout_count=0,stderr_count=0;
 const lc_command_slot *selected=&a->slots[slot];
 lr_command command={selected->executable,selected->working_directory,selected->arguments,selected->argument_count,selected->environment,selected->environment_count,selected->output_limit};
 if(!f->effect(slot,&command,&exit_code,&stdout_bytes,&stdout_count,&stderr_bytes,&stderr_count)){errno=EIO;return -1;}
 int new_out=source_memfd(stdout_bytes,stdout_count),new_err=source_memfd(stderr_bytes,stderr_count),leader=eventfd(0,EFD_CLOEXEC|EFD_NONBLOCK);
 if(new_out<0||new_err<0||leader<0){if(new_out>=0)close(new_out);if(new_err>=0)close(new_err);if(leader>=0)close(leader);return -1;}
 close(out[0]);close(err[0]);out[0]=new_out;err[0]=new_err;
 *pidfd=leader;f->leader_fd[slot]=leader;f->leader_exit[slot]=exit_code;f->leader_ready[slot]=1;f->births++;
 (void)handshake;
 /* Positive test diagnostic only; never an actual PID or custody claim. */
 return (long)slot+1;
}
static int source_waitid(idtype_t type,id_t fd,siginfo_t *info,int flags) {
 struct source_fixture *f=source_current;
 if(!f||type!=P_PIDFD||flags!=(WEXITED|WNOHANG)){errno=EINVAL;return -1;}
 for(unsigned i=0;i<70;i++)if(f->leader_fd[i]==(int)fd){if(f->leader_ready[i]){info->si_pid=(pid_t)i+1;info->si_code=CLD_EXITED;info->si_status=f->leader_exit[i];}return 0;}
 errno=EBADF;return -1;
}
static int source_signal(int fd,int signal_number) {
 struct source_fixture *f=source_current;if(!f||signal_number!=SIGTERM){errno=EINVAL;return -1;}
 for(unsigned i=0;i<70;i++)if(f->leader_fd[i]==fd){f->leader_ready[i]=1;f->leader_exit[i]=130;f->signals++;return 0;}
 errno=EBADF;return -1;
}
static cc_view source_view(const char *value){return (cc_view){(const unsigned char *)value,strlen(value)};}
static int source_capability(int fd,lc_capability *cap) {
 struct stat st;struct statfs fs;struct statx x;
 if(fd<0||fstat(fd,&st)||fstatfs(fd,&fs)||statx(fd,"",AT_EMPTY_PATH,STATX_MNT_ID,&x)||(x.stx_mask&STATX_MNT_ID)==0)return 0;
 *cap=(lc_capability){.version=1,.size=sizeof *cap,.descriptor=fd,.device=st.st_dev,.inode=st.st_ino,.mount_identity=x.stx_mnt_id,.expected_file_system=fs.f_type,.access=O_RDONLY};return 1;
}
int fsgg_caller_source_layout(size_t view_size,size_t environment_size,size_t command_size,size_t step_size,size_t recipe_size,size_t target_offset,size_t steps_offset,size_t removals_offset) {
 return view_size==sizeof(cc_view)&&environment_size==sizeof(lc_environment)&&command_size==sizeof(lr_command)&&step_size==sizeof(lr_step)&&recipe_size==sizeof(lr_recipe)&&target_offset==offsetof(lr_recipe,target)&&steps_offset==offsetof(lr_recipe,steps)&&removals_offset==offsetof(lr_recipe,removal_prefix);
}
int fsgg_caller_source_open(const lr_recipe *recipe,uint64_t deadline_utc_ms,int channel_fd,source_effect effect) {
 if(source_current||!recipe||!effect||channel_fd<0||!lr_validate(recipe,NULL))return 0;
 uint64_t utc,boot;if(!clocks(&utc,&boot)||deadline_utc_ms<=utc||deadline_utc_ms-utc>120000)return 0;
 struct source_fixture *f=calloc(1,sizeof *f);lc_adapter *a=calloc(1,sizeof *a);
 if(!f||!a){free(f);free(a);return 0;}
 f->adapter=a;f->effect=effect;for(unsigned i=0;i<70;i++)f->leader_fd[i]=-1;
 a->requester_pidfd=-1;a->active=LC_NONE;
 for(unsigned i=0;i<2;i++)a->root_parent[i]=a->root_directory[i]=a->bundle_directory[i]=-1;
 for(unsigned i=0;i<72;i++)a->caps[i].descriptor=-1;
 for(unsigned i=0;i<70;i++){a->owned[i].pidfd=a->owned[i].out=a->owned[i].err=a->owned[i].events=a->owned[i].kill=a->owned[i].exec_error=-1;}
 a->operation.command_count=recipe->maximum_commands;a->operation.handle_count=recipe->maximum_commands+2;
 a->operation.maximum_commands=recipe->maximum_commands;a->operation.max_active_commands=1;a->operation.memory_max_bytes=536870912;
 a->operation.admitted_utc_ms=utc;a->operation.deadline_utc_ms=deadline_utc_ms;a->operation.admitted_boot_ns=boot;a->operation.deadline_boot_ns=boot+(deadline_utc_ms-utc)*UINT64_C(1000000);a->operation.cleanup_reserve_ns=UINT64_C(1000000000);a->operation.aggregate_output_bytes=16u*1024u*1024u;
 /* identity_class remains zero: this is not admitted production/native authority. */
 a->root_recipe=lr_copy_recipe(recipe);if(!a->root_recipe)goto failed;
 const lr_recipe *copied=lr_copied_recipe(a->root_recipe);if(!lr_init(&a->root_state,copied,NULL))goto failed;
 a->root_uid=getuid();a->root_gid=getgid();a->root_binding.recipe=copied;
 const cc_view paths[]={copied->work_root,copied->evidence_root};
 for(unsigned i=0;i<2;i++) {
  if(paths[i].size>=PATH_MAX)goto failed;
  memcpy(f->parents[i],paths[i].data,paths[i].size);f->parents[i][paths[i].size]=0;
  char *split=strrchr(f->parents[i],'/');if(!split||split==f->parents[i]||!split[1])goto failed;
  strcpy(f->names[i],split+1);*split=0;
  a->root_parent[i]=open(f->parents[i],O_RDONLY|O_DIRECTORY|O_CLOEXEC|O_NOFOLLOW);if(a->root_parent[i]<0)goto failed;
  lc_capability cap;if(!source_capability(a->root_parent[i],&cap)||!verify_root_parent(&cap,a->root_uid,a->root_gid))goto failed;
  if(i==0){a->root_binding.work_parent=cap;a->root_binding.work_parent_path=source_view(f->parents[i]);a->root_binding.work_name=source_view(f->names[i]);}
  else {a->root_binding.evidence_parent=cap;a->root_binding.evidence_parent_path=source_view(f->parents[i]);a->root_binding.evidence_name=source_view(f->names[i]);}
 }
 a->strings=malloc(LC_STRING_MAX);a->reply=malloc(CC_FRAME_MAX+4u);f->frame=malloc(CC_FRAME_MAX);f->text=malloc(CC_FRAME_MAX);f->nodes=calloc(65536,sizeof(cc_node));
 if(!a->strings||!a->reply||!f->frame||!f->text||!f->nodes)goto failed;
 for(unsigned i=0;i<recipe->maximum_commands;i++) {
  snprintf(f->lease_ids[i],65,"%064x",i+1);
  a->slots[i].command_identity=source_view(f->lease_ids[i]);a->slots[i].output_limit=CC_OUTPUT_MAX;a->slots[i].directory_handle=i;
  a->caps[i].descriptor=fcntl(a->root_parent[0],F_DUPFD_CLOEXEC,3);if(a->caps[i].descriptor<0)goto failed;
  a->slots[i].cgroup_handle=i;
  /* Poll/kill tokens are inert ordinary memfds; no cgroup is supplied. */
  a->owned[i].events=source_memfd((const unsigned char *)"populated 0\n",12);
  a->owned[i].kill=source_memfd(NULL,0);
  if(a->owned[i].events<0||a->owned[i].kill<0)goto failed;
 }
 a->channel=(int)recipe->maximum_commands;a->keeper=a->channel+1;
 a->caps[a->channel].descriptor=fcntl(channel_fd,F_DUPFD_CLOEXEC,3);
 a->caps[a->keeper].descriptor=eventfd(0,EFD_CLOEXEC|EFD_NONBLOCK);
 if(a->caps[a->channel].descriptor<0||a->caps[a->keeper].descriptor<0)goto failed;
 cc_workspace w={.frame=f->frame,.text=f->text,.frame_capacity=CC_FRAME_MAX,.text_capacity=CC_FRAME_MAX,.nodes=f->nodes,.node_capacity=65536};
 if(!cc_init(&a->core,w,f->leases,70,copied->custody_identity,copied->custody_identity,copied->expected_input_sha256,utc,deadline_utc_ms,a->operation.aggregate_output_bytes))goto failed;
 source_current=f;return 1;
 failed:dispose_storage(a);free(f->frame);free(f->text);free(f->nodes);free(f);return 0;
}
int fsgg_caller_source_serve(void) {
 struct source_fixture *f=source_current;if(!f||atomic_exchange(&f->serving,1))return 0;
 int ok=lc_serve(f->adapter);atomic_store(&f->serving,0);return ok;
}
int fsgg_caller_source_trace(uint32_t *slots,uint32_t *births,uint32_t *signals,int *aborted) {
 struct source_fixture *f=source_current;if(!f||!slots||!births||!signals||!aborted||atomic_load(&f->serving))return 0;
 *slots=f->adapter->next_slot;*births=f->births;*signals=f->signals;
 return lc_root_custody_outcome(f->adapter,aborted);
}
int fsgg_caller_source_receive(const unsigned char *frame,size_t bytes,unsigned char *reply,size_t capacity) {
 struct source_fixture *f=source_current;if(!f||!frame||!reply||capacity<4)return 0;
 uint64_t utc,boot;if(!clocks(&utc,&boot))return 0;
 cc_progress parsed=cc_feed(&f->adapter->core,frame,bytes,utc);if(parsed==CC_MORE)return -1;if(parsed!=CC_READY)return 0;
 size_t count=0;if(!perform(f->adapter,&count)||count>capacity||count>INT_MAX)return 0;
 memcpy(reply,f->adapter->reply,count);return (int)count;
}
int fsgg_caller_source_eof(void) {
 struct source_fixture *f=source_current;if(!f)return 0;lc_adapter *a=f->adapter;
 if(a->core.pending||a->core.header_used||a->core.frame_used||!root_eof_terminal(a))return 0;
 for(unsigned i=0;i<a->next_slot;i++)if(!a->owned[i].born||!a->owned[i].settled)return 0;
 return !a->failure&&lr_terminal(&a->root_state);
}
int fsgg_caller_source_close(void) {
 struct source_fixture *f=source_current;if(!f)return 1;if(atomic_load(&f->serving))return 0;
 /* No actual process was born by this distinct translation unit. Its callback
  * is synchronous and the managed owner joins the only peer before this call. */
 dispose_storage(f->adapter);free(f->frame);free(f->text);free(f->nodes);free(f);source_current=NULL;return 1;
}

#ifndef FSGG_CALLER_SOURCE_LIBRARY
int main(void) {
 if(!fsgg_caller_source_layout(16,32,72,88,464,72,144,168))return 1;
 if(fsgg_caller_source_serve()||fsgg_caller_source_eof())return 1;
 if(!fsgg_caller_source_close())return 1;
 puts("PASS native recipe ABI and unopened source-fixture refusal; kernel effects NOT_RUN");return 0;
}
#endif
