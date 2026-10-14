#define _GNU_SOURCE
#include "custody_adapter.h"
#include "custody_root.h"
#include <errno.h>
#include <fcntl.h>
#include <grp.h>
#include <linux/magic.h>
#include <linux/nsfs.h>
#include <linux/openat2.h>
#include <linux/sched.h>
#include <poll.h>
#include <signal.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/ioctl.h>
#include <sys/prctl.h>
#include <sys/socket.h>
#include <sys/stat.h>
#include <sys/statfs.h>
#include <sys/statvfs.h>
#include <sys/syscall.h>
#include <sys/types.h>
#include <sys/wait.h>
#include <time.h>
#include <unistd.h>

#define LC_FD_MAX 65536u
#define LC_NS_UID 32768u
#define LC_NONE UINT32_MAX
struct lc_owned {
 int born,pidfd,events,kill,out,err,exec_error,exit_known,exit_code,signal,empty,out_eof,err_eof,settled,leader_left_descendants;
 uint64_t captured,stdout_captured,stderr_captured,escaped,acknowledged;
 size_t out_size,err_size;
 unsigned char *out_bytes,*err_bytes;
 int orphan_pidfds[32];
 long orphan_diagnostics[32];
 unsigned orphan_count,orphan_reaped;
};
struct lc_adapter {
 lc_operation operation;
 lc_command_slot slots[LC_SLOT_MAX];
 lc_capability caps[LC_CAP_MAX];
 cc_view vectors[LC_VECTOR_MAX];
 lc_environment environment[LC_VECTOR_MAX];
 char *strings;
 size_t string_used,vector_used,environment_used;
 char **argv[LC_SLOT_MAX],**envp[LC_SLOT_MAX];
 cc_core core;
 struct lc_owned owned[LC_SLOT_MAX];
 uint32_t next_slot,active;
 int channel,user_ns,mount_ns,keeper,operation_group,control_leaf,old_subreaper,subreaper_changed,requester_pidfd;
 long requester_diagnostic;
 uint64_t pending_capture;
 unsigned char *reply;
 const char *failure;
 int served,terminal,partial_acknowledgment;
 lr_recipe_copy *root_recipe; lr_state root_state; lr_binding root_binding;
 int root_parent[2],root_directory[2],bundle_directory[2];
 uint32_t root_uid,root_gid,root_census_slot[LR_STORE_COUNT]; unsigned root_bound[LC_SLOT_MAX],root_reported[LC_SLOT_MAX];
};
static int refuse(const char **failure,const char *reason) { if(failure)*failure=reason;
 return 0;
 }
static int same(cc_view a,cc_view b) { return a.size==b.size&&(!a.size||(a.data&&b.data&&memcmp(a.data,b.data,a.size)==0));
 }
static int literal(cc_view a,const char *b) { return a.size==strlen(b)&&(!a.size||(a.data&&memcmp(a.data,b,a.size)==0));
 }
static int digest(cc_view a) {
 size_t i;
 if(!a.data||a.size!=64)return 0;
 for(i=0;i<64;i++)if(!((a.data[i]>='0'&&a.data[i]<='9')||(a.data[i]>='a'&&a.data[i]<='f')))return 0;
 return 1;
}
static int string(cc_view a) {
 size_t i=0;
 if(a.size&&!a.data)return 0;
 while(i<a.size) {
  unsigned char b=a.data[i++];
  unsigned n;
  uint32_t cp,min;
  if(!b)return 0;
  if(b<128)continue;
  if(b>=0xc2&&b<=0xdf){n=1;
   cp=b&31u;
   min=128;
   }
  else if(b>=0xe0&&b<=0xef){n=2;
   cp=b&15u;
   min=2048;
   }
  else if(b>=0xf0&&b<=0xf4){n=3;
   cp=b&7u;
   min=65536;
   }else return 0;
  if(n>a.size-i)return 0;
  while(n--){b=a.data[i++];
   if((b&0xc0u)!=0x80u)return 0;
   cp=(cp<<6)|(b&63u);
   }
  if(cp<min||cp>0x10ffffu||(cp>=0xd800u&&cp<=0xdfffu))return 0;
 }
 return 1;
}
static int count_string(cc_view s,size_t *total) {
 if(!string(s)||s.size>=LC_STRING_MAX||*total>LC_STRING_MAX-s.size-1)return 0;
 *total+=s.size+1;
 return 1;
}
static int cap_role(const lc_operation *o,uint32_t index,lc_role role,uint32_t slot) {
 return index<o->handle_count&&o->handles[index].role==role&&o->handles[index].slot==slot;
}
int lc_validate(const lc_operation *o,const char **failure) {
 size_t i,j,k,total=0,vectors=0,envs=0;
 unsigned roles[12]={0};
 if(!o||o->version!=LC_VERSION||o->size!=sizeof *o)return refuse(failure,"operation-layout");
 if(!literal(o->schema,LC_SCHEMA)||!digest(o->operation_id)||!digest(o->custody_identity)||
  !digest(o->admission_sha256)||!digest(o->expected_input_sha256)||!digest(o->source_sha256)||
  !digest(o->adapter_binary_sha256)||!digest(o->toolchain_closure_sha256)||!digest(o->native_profile_sha256))return refuse(failure,"operation-identity");
 if(o->identity_class!=LC_PRODUCTION&&o->identity_class!=LC_CONTROLLED_NATIVE)return refuse(failure,"operation-class");
 if(o->deadline_utc_ms<=o->admitted_utc_ms||o->deadline_utc_ms-o->admitted_utc_ms>5400000u||
  o->deadline_boot_ns<=o->admitted_boot_ns||o->deadline_boot_ns-o->admitted_boot_ns>UINT64_C(5400000000000)||
  o->deadline_boot_ns-o->admitted_boot_ns!=(o->deadline_utc_ms-o->admitted_utc_ms)*1000000u||
  !o->cleanup_reserve_ns||o->cleanup_reserve_ns>=o->deadline_boot_ns-o->admitted_boot_ns)return refuse(failure,"operation-deadline");
 if(o->max_owned_tasks!=32||o->max_active_commands!=1||!o->aggregate_output_bytes||!o->memory_max_bytes||
  !o->cpu_max_micros||!o->cpu_period_micros||o->cpu_max_micros>o->cpu_period_micros||
  !o->maximum_commands||o->maximum_commands>LC_SLOT_MAX||o->command_count!=o->maximum_commands||
  !o->command_slots||!o->handles||o->handle_count<8||o->handle_count>LC_CAP_MAX)return refuse(failure,"operation-bounds");
 total=sizeof *o+o->command_count*sizeof(lc_command_slot)+o->handle_count*sizeof(lc_capability);
 if(total>=LC_STRING_MAX)return refuse(failure,"operation-table-bound");
 if(!count_string(o->schema,&total)||!count_string(o->operation_id,&total)||!count_string(o->custody_identity,&total)||
  !count_string(o->admission_sha256,&total)||!count_string(o->expected_input_sha256,&total)||!count_string(o->source_sha256,&total)||
  !count_string(o->adapter_binary_sha256,&total)||!count_string(o->toolchain_closure_sha256,&total)||!count_string(o->native_profile_sha256,&total))return refuse(failure,"operation-string-bound");
 for(i=0;i<o->handle_count;i++) {
  const lc_capability *c=&o->handles[i];
  if(c->version!=LC_VERSION||c->size!=sizeof *c||c->role<LC_OPERATION_CGROUP||c->role>LC_NAMESPACE_KEEPER||
   c->descriptor<3||c->descriptor>=(int)LC_FD_MAX||!c->inode||!c->mount_identity||
   (c->access!=O_RDONLY&&c->access!=O_RDWR))return refuse(failure,"capability-layout");
  for(j=0;j<i;j++)if(o->handles[j].descriptor==c->descriptor||(o->handles[j].role==c->role&&o->handles[j].slot==c->slot)||
   (c->role<=LC_COMMAND_CGROUP&&o->handles[j].role<=LC_COMMAND_CGROUP&&c->device==o->handles[j].device&&c->inode==o->handles[j].inode))return refuse(failure,"capability-duplicate");
  if(c->role!=LC_CHANNEL&&c->role!=LC_NAMESPACE_KEEPER&&c->access!=O_RDONLY)return refuse(failure,"capability-readonly-role");
  roles[c->role]++;
  if((c->role==LC_COMMAND_CGROUP||c->role==LC_EXECUTABLE||c->role==LC_SUPERVISOR||c->role==LC_DIRECTORY)?c->slot>=o->command_count:c->slot!=LC_NONE)return refuse(failure,"capability-slot");
 }
 for(i=LC_OPERATION_CGROUP;i<=LC_NAMESPACE_KEEPER;i++) {
  unsigned expected=(i==LC_COMMAND_CGROUP||i==LC_EXECUTABLE||i==LC_SUPERVISOR||i==LC_DIRECTORY)?o->maximum_commands:1;
  if(roles[i]!=expected)return refuse(failure,"capability-role-count");
 }
 for(i=0;i<o->command_count;i++) {
  const lc_command_slot *s=&o->command_slots[i];
  if(s->version!=LC_VERSION||s->size!=sizeof *s||s->slot!=i||!digest(s->command_identity)||
   !literal(s->executable,"/usr/sbin/podman")||!s->working_directory.data||!s->working_directory.size||s->working_directory.data[0]!='/'||
   s->output_limit!=CC_OUTPUT_MAX||s->argument_count>LC_VECTOR_MAX-vectors||s->environment_count>LC_VECTOR_MAX-envs||
   (s->argument_count&&!s->arguments)||(s->environment_count&&!s->environment))return refuse(failure,"command-slot");
  for(j=0;j<i;j++)if(same(o->command_slots[j].command_identity,s->command_identity))return refuse(failure,"command-identity-reuse");
  if(!cap_role(o,s->executable_handle,LC_EXECUTABLE,(uint32_t)i)||!cap_role(o,s->supervisor_handle,LC_SUPERVISOR,(uint32_t)i)||
   !cap_role(o,s->directory_handle,LC_DIRECTORY,(uint32_t)i)||!cap_role(o,s->cgroup_handle,LC_COMMAND_CGROUP,(uint32_t)i))return refuse(failure,"command-capability-join");
  if(!count_string(s->command_identity,&total)||!count_string(s->executable,&total)||!count_string(s->working_directory,&total))return refuse(failure,"command-string-bound");
  for(j=0;j<s->argument_count;j++)if(!count_string(s->arguments[j],&total))return refuse(failure,"argument-string");
  for(j=0;j<s->environment_count;j++) {
   const lc_environment *e=&s->environment[j];
   if(!e->name.size||!count_string(e->name,&total)||!count_string(e->value,&total)||memchr(e->name.data,'=',e->name.size))return refuse(failure,"environment-string");
   for(k=0;k<j;k++)if(same(e->name,s->environment[k].name))return refuse(failure,"environment-duplicate");
   if(total>LC_STRING_MAX-e->name.size-e->value.size-2)return refuse(failure,"environment-exec-bound");
   total+=e->name.size+e->value.size+2;
  }
  size_t tables=s->argument_count*sizeof(cc_view)+s->environment_count*sizeof(lc_environment);
  if(tables>LC_STRING_MAX-total)return refuse(failure,"operation-vector-table-bound");
  total+=tables;
  vectors+=s->argument_count;
  envs+=s->environment_count;
 }
 if(failure)*failure=NULL;
 return 1;
}
static int fail(lc_adapter *a,const char *reason) {
 if(!a->failure)a->failure=reason;
 cc_poison(&a->core,reason);
 return 0;
}
static int clocks(uint64_t *utc,uint64_t *boot) {
 struct timespec u,b;
 if(clock_gettime(CLOCK_REALTIME,&u)||clock_gettime(CLOCK_BOOTTIME,&b)||u.tv_sec<0||b.tv_sec<0)return 0;
 if((uint64_t)u.tv_sec>UINT64_MAX/1000u||(uint64_t)b.tv_sec>UINT64_MAX/1000000000u)return 0;
 *utc=(uint64_t)u.tv_sec*1000u+(uint64_t)u.tv_nsec/1000000u;
 *boot=(uint64_t)b.tv_sec*1000000000u+(uint64_t)b.tv_nsec;
 return 1;
}
static int alive(lc_adapter *a,int new_start,uint64_t *utc) {
 uint64_t boot;
 if(!clocks(utc,&boot))return fail(a,"clock-observation");
 if(*utc<a->operation.admitted_utc_ms||*utc>=a->operation.deadline_utc_ms||boot<a->operation.admitted_boot_ns||boot>=a->operation.deadline_boot_ns)return fail(a,"original-deadline-expired");
 if(new_start&&a->operation.deadline_boot_ns-boot<=a->operation.cleanup_reserve_ns)return fail(a,"cleanup-reserve");
 return 1;
}
static cc_view copy(lc_adapter *a,cc_view v) {
 cc_view result={NULL,0};
 if(v.size>=LC_STRING_MAX||a->string_used>LC_STRING_MAX-v.size-1)return result;
 result.data=(unsigned char *)a->strings+a->string_used;
 result.size=v.size;
 memcpy((void *)result.data,v.data,v.size);
 a->strings[a->string_used+v.size]=0;
 a->string_used+=v.size+1;
 return result;
}
static int read_at(int fd,const char *name,char *out,size_t capacity) {
 int input=openat(fd,name,O_RDONLY|O_CLOEXEC|O_NOFOLLOW);
 ssize_t n,extra;
 char c;
 if(input<0)return 0;
 n=read(input,out,capacity-1);
 extra=n<0?-1:read(input,&c,1);
 close(input);
 if(n<0||extra!=0)return 0;
 out[n]=0;
 return 1;
}
static int file_equal(int fd,const char *name,const char *expected) {
 char b[256];
 return read_at(fd,name,b,sizeof b)&&strcmp(b,expected)==0;
}
static int number_equal(int fd,const char *name,uint64_t expected) {
 char b[128],e[128];
 if(snprintf(e,sizeof e,"%llu\n",(unsigned long long)expected)<0)return 0;
 return read_at(fd,name,b,sizeof b)&&strcmp(b,e)==0;
}
static int events_populated(const char *text,int *value) {
 int parsed=-1;
 if(!text||!value)return 0;
 for(const char *line=text;*line;){
  const char *end=strchr(line,'\n');
  if(!end)return 0;
  size_t size=(size_t)(end-line);
  if(size>=10&&!memcmp(line,"populated ",10)){
   if(parsed>=0||size!=11||(line[10]!='0'&&line[10]!='1'))return 0;
   parsed=line[10]=='1';
  }
  line=end+1;
 }
 if(parsed<0)return 0;
 *value=parsed;
 return 1;
}
static int populated(int fd,int *value) {
 char b[256];
 return read_at(fd,"cgroup.events",b,sizeof b)&&events_populated(b,value);
}
static int pidfd_alive(int fd) {
 struct pollfd p={fd,POLLIN,0};
 return poll(&p,1,0)==0;
}
static int pidfd_number(int fd,long *number) {
 char path[64],info[1024];
 int input;
 ssize_t n;
 if(snprintf(path,sizeof path,"/proc/self/fdinfo/%d",fd)<0)return 0;
 input=open(path,O_RDONLY|O_CLOEXEC|O_NOFOLLOW);
 if(input<0)return 0;
 n=read(input,info,sizeof info-1);
 close(input);
 if(n<=0||n>=(ssize_t)sizeof info-1)return 0;
 info[n]=0;
 char *p=strstr(info,"Pid:\t");
 return p&&sscanf(p+5,"%ld",number)==1&&*number>0;
}
static int maps(const char *s) {
 unsigned long inside[3],outside[3],count[3];
 size_t n=0;
 const char *p=s;
 int used;
 while(*p&&n<3) {
  if(sscanf(p,"%lu %lu %lu%n",&inside[n],&outside[n],&count[n],&used)!=3||used<=0)return 0;
  p+=used;
  if(*p!='\n')return 0;
  p++;
  n++;
 }
 return !*p&&n==2&&inside[0]==0&&count[0]==1&&outside[0]>0&&
 inside[1]==LC_NS_UID&&count[1]==1&&outside[1]>0&&outside[0]!=outside[1];
}
static int membership(long,int);
static int visible(lc_adapter *a,int proc,uint32_t slot) {
 const lc_command_slot *s=&a->slots[slot];
 const char *paths[]={"usr/sbin/podman","usr/bin/setsid",(const char *)s->working_directory.data+1};
 uint32_t caps[]={s->executable_handle,s->supervisor_handle,s->directory_handle};
 int root=openat(proc,"root",O_RDONLY|O_DIRECTORY|O_CLOEXEC);
 if(root<0)return 0;
 for(unsigned i=0;i<3;i++){struct stat seen;
  struct statvfs mount;
  int fd;
  if(a->root_recipe){struct open_how how={.flags=O_RDONLY|O_CLOEXEC|(i==2?O_DIRECTORY:0),.resolve=RESOLVE_BENEATH|RESOLVE_NO_SYMLINKS|RESOLVE_NO_MAGICLINKS};fd=(int)syscall(SYS_openat2,root,paths[i],&how,sizeof how);}
  else fd=openat(root,paths[i],O_RDONLY|O_CLOEXEC|(i==2?O_DIRECTORY:0));
  if(fd<0){close(root);
   return 0;
   }
  int ok=fstat(fd,&seen)==0&&(uint64_t)seen.st_dev==a->caps[caps[i]].device&&(uint64_t)seen.st_ino==a->caps[caps[i]].inode;
  /* Work directory may be writable; selected executable/loader mounts must be sealed by the owning profile. */
  if(i<2)ok=ok&&fstatvfs(fd,&mount)==0&&(mount.f_flag&ST_RDONLY)!=0;
  close(fd);
  if(!ok){close(root);
   return 0;
   }
 }
 close(root);
 return 1;
}
static int verify_root_parent(const lc_capability *c,uint32_t uid,uint32_t gid) {
 struct stat s;struct statfs fs;struct statx x;int flags=fcntl(c->descriptor,F_GETFL);
 return flags>=0&&(flags&O_ACCMODE)==O_RDONLY&&fstat(c->descriptor,&s)==0&&fstatfs(c->descriptor,&fs)==0&&
  S_ISDIR(s.st_mode)&&s.st_uid==uid&&s.st_gid==gid&&!(s.st_mode&0022)&&
  (uint64_t)s.st_dev==c->device&&(uint64_t)s.st_ino==c->inode&&(uint64_t)fs.f_type==c->expected_file_system&&
  statx(c->descriptor,"",AT_EMPTY_PATH,STATX_MNT_ID,&x)==0&&(x.stx_mask&STATX_MNT_ID)&&x.stx_mnt_id==c->mount_identity;
}
static int root_parent_visible(lc_adapter *a,int proc) {
 const lc_capability *caps[]={&a->root_binding.work_parent,&a->root_binding.evidence_parent};
 const cc_view paths[]={a->root_binding.work_parent_path,a->root_binding.evidence_parent_path};
 int root=openat(proc,"root",O_RDONLY|O_DIRECTORY|O_CLOEXEC);if(root<0)return 0;
 int ok=1;
 for(unsigned i=0;i<2;i++) {
  struct stat st;struct statx x;struct statfs fs;
  struct open_how how={.flags=O_RDONLY|O_DIRECTORY|O_CLOEXEC,.resolve=RESOLVE_BENEATH|RESOLVE_NO_SYMLINKS|RESOLVE_NO_MAGICLINKS};
  int fd=(int)syscall(SYS_openat2,root,(const char *)paths[i].data+1,&how,sizeof how);
  if(fd<0||fstat(fd,&st)||fstatfs(fd,&fs)||statx(fd,"",AT_EMPTY_PATH,STATX_MNT_ID,&x)||
   st.st_uid!=a->root_uid||st.st_gid!=a->root_gid||(st.st_mode&0022)||(uint64_t)st.st_dev!=caps[i]->device||(uint64_t)st.st_ino!=caps[i]->inode||
   (uint64_t)fs.f_type!=caps[i]->expected_file_system||(x.stx_mask&STATX_MNT_ID)==0||x.stx_mnt_id!=caps[i]->mount_identity)ok=0;
  if(fd>=0)close(fd);
 }
 close(root);return ok;
}
static int namespace_join(lc_adapter *a,int visible_slots) {
 char path[64],info[1024],uid[512],gid[512];
 int fd=-1,proc=-1,ns=-1,ok=0;
 long pid=0;
 struct stat given,observed;
 if(!pidfd_alive(a->caps[a->keeper].descriptor))return 0;
 if(snprintf(path,sizeof path,"/proc/self/fdinfo/%d",a->caps[a->keeper].descriptor)<0)return 0;
 fd=open(path,O_RDONLY|O_CLOEXEC|O_NOFOLLOW);
 if(fd<0)return 0;
 {ssize_t n=read(fd,info,sizeof info-1);
  close(fd);
  if(n<=0||n>=(ssize_t)sizeof info-1)return 0;
  info[n]=0;
  }
 {char *p=strstr(info,"Pid:\t");
  if(!p||sscanf(p+5,"%ld",&pid)!=1||pid<=0)return 0;
  }
 if(snprintf(path,sizeof path,"/proc/%ld",pid)<0)return 0;
 proc=open(path,O_RDONLY|O_DIRECTORY|O_CLOEXEC|O_NOFOLLOW);
 if(proc<0)return 0;
 ns=openat(proc,"ns/user",O_RDONLY|O_CLOEXEC);
 if(ns<0||fstat(ns,&observed)||fstat(a->caps[a->user_ns].descriptor,&given)||given.st_ino!=observed.st_ino||given.st_dev!=observed.st_dev)goto done;
 close(ns);
 ns=openat(proc,"ns/mnt",O_RDONLY|O_CLOEXEC);
 if(ns<0||fstat(ns,&observed)||fstat(a->caps[a->mount_ns].descriptor,&given)||given.st_ino!=observed.st_ino||given.st_dev!=observed.st_dev)goto done;
 if(!read_at(proc,"uid_map",uid,sizeof uid)||!read_at(proc,"gid_map",gid,sizeof gid)||!maps(uid)||!maps(gid))goto done;
 if(!membership(pid,a->caps[a->control_leaf].descriptor))goto done;
 if(a->root_recipe){uint32_t u,g;
  if(!lr_mapped_uid0(uid,&u)||!lr_mapped_uid0(gid,&g)||
   (a->root_uid&&(a->root_uid!=u||a->root_gid!=g)))goto done;
  a->root_uid=u;a->root_gid=g;
  if(!root_parent_visible(a,proc))goto done;
 }
 if(visible_slots)for(uint32_t i=0;i<a->operation.command_count;i++) {
  if(a->root_recipe&&(!a->root_bound[i]||(i!=a->next_slot-1&&(!a->owned[i].born||a->owned[i].settled))))continue;
  if(!visible(a,proc,i))goto done;
 }
 ok=pidfd_alive(a->caps[a->keeper].descriptor);
 done:if(ns>=0)close(ns);
 close(proc);
 return ok;
}
static int ancestor(int child,int parent) {
 struct stat p,c;
 int at=fcntl(child,F_DUPFD_CLOEXEC,3),ok=0;
 unsigned depth;
 if(at<0||fstat(parent,&p)){if(at>=0)close(at);
  return 0;
  }
 for(depth=0;depth<64;depth++) {
  int next;
  if(fstat(at,&c))break;
  if(c.st_dev==p.st_dev&&c.st_ino==p.st_ino){ok=1;
   break;
   }
  next=openat(at,"..",O_RDONLY|O_DIRECTORY|O_CLOEXEC|O_NOFOLLOW);
  if(next<0)break;
  {struct stat n;
   if(fstat(next,&n)||(n.st_dev==c.st_dev&&n.st_ino==c.st_ino)){close(next);
    break;
    }}
  close(at);
  at=next;
 }
 close(at);
 return ok;
}
static int membership(long pid,int group) {
 char linkpath[64],leaf[4096],root[4096],path[64],actual[4096],expected[4096];
 struct stat current,next;
 int at=fcntl(group,F_DUPFD_CLOEXEC,3),proc=-1;
 unsigned depth;
 ssize_t n;
 if(at<0)return 0;
 for(depth=0;depth<64;depth++) {
  int parent;
  if(fstat(at,&current)){close(at);
   return 0;
   }
  parent=openat(at,"..",O_RDONLY|O_DIRECTORY|O_CLOEXEC|O_NOFOLLOW);
  if(parent<0||fstat(parent,&next)){if(parent>=0)close(parent);
   close(at);
   return 0;
   }
  if(next.st_dev!=current.st_dev||next.st_ino==current.st_ino){close(parent);
   break;
   }
  close(at);
  at=parent;
 }
 if(depth==64){close(at);
  return 0;
  }
 if(snprintf(linkpath,sizeof linkpath,"/proc/self/fd/%d",at)<0){close(at);
  return 0;
  }
 n=readlink(linkpath,root,sizeof root-1);
 close(at);
 if(n<0||n>=(ssize_t)sizeof root-1)return 0;
 root[n]=0;
 if(snprintf(linkpath,sizeof linkpath,"/proc/self/fd/%d",group)<0)return 0;
 n=readlink(linkpath,leaf,sizeof leaf-1);
 if(n<0||n>=(ssize_t)sizeof leaf-1)return 0;
 leaf[n]=0;
 if(strncmp(leaf,root,strlen(root))||leaf[strlen(root)]!='/')return 0;
 if(snprintf(expected,sizeof expected,"0::%s\n",leaf+strlen(root))>=(int)sizeof expected)return 0;
 if(snprintf(path,sizeof path,"/proc/%ld",pid)<0)return 0;
 proc=open(path,O_RDONLY|O_DIRECTORY|O_CLOEXEC|O_NOFOLLOW);
 if(proc<0)return 0;
 int ok=read_at(proc,"cgroup",actual,sizeof actual)&&strcmp(actual,expected)==0;
 close(proc);
 return ok;
}
static int elf64(int fd) {
 unsigned char b[64];
 return pread(fd,b,sizeof b,0)==(ssize_t)sizeof b&&b[0]==0x7f&&b[1]=='E'&&b[2]=='L'&&b[3]=='F'&&
  b[4]==2&&b[5]==1&&b[6]==1&&(b[16]==2||b[16]==3)&&b[17]==0&&b[18]==62&&b[19]==0;
}
static int protected_controls(int fd) {
 const char *names[]={"cgroup.procs","cgroup.threads","cgroup.subtree_control","cgroup.type","cgroup.kill","pids.max","memory.max","cpu.max"};
 for(size_t i=0;i<sizeof names/sizeof names[0];i++){struct stat s;
  if(fstatat(fd,names[i],&s,AT_SYMLINK_NOFOLLOW)||!S_ISREG(s.st_mode)||s.st_uid!=0||(s.st_mode&0022))return 0;
 }
 return 1;
}
static int verify_cap(const lc_capability *c) {
 struct stat s;
 struct statfs fs;
 struct statx x;
 int flags=fcntl(c->descriptor,F_GETFL);
 if(flags<0||(flags&O_ACCMODE)!=c->access||fstat(c->descriptor,&s)||fstatfs(c->descriptor,&fs)||
  (uint64_t)s.st_dev!=c->device||(uint64_t)s.st_ino!=c->inode||(uint64_t)fs.f_type!=c->expected_file_system||
  statx(c->descriptor,"",AT_EMPTY_PATH|AT_STATX_SYNC_AS_STAT,STATX_MNT_ID,&x)||(x.stx_mask&STATX_MNT_ID)==0||x.stx_mnt_id!=c->mount_identity)return 0;
 switch(c->role) {
 case LC_OPERATION_CGROUP:case LC_CONTROL_LEAF:case LC_REQUESTER_LEAF:case LC_COMMAND_CGROUP:
  return S_ISDIR(s.st_mode)&&fs.f_type==CGROUP2_SUPER_MAGIC&&s.st_uid==0&&!(s.st_mode&0022)&&file_equal(c->descriptor,"cgroup.type","domain\n")&&protected_controls(c->descriptor);
 case LC_USER_NAMESPACE:return fs.f_type==NSFS_MAGIC&&ioctl(c->descriptor,NS_GET_NSTYPE)==CLONE_NEWUSER;
 case LC_MOUNT_NAMESPACE:return fs.f_type==NSFS_MAGIC&&ioctl(c->descriptor,NS_GET_NSTYPE)==CLONE_NEWNS;
 case LC_EXECUTABLE:case LC_SUPERVISOR:return S_ISREG(s.st_mode)&&s.st_uid==0&&!(s.st_mode&0022)&&(s.st_mode&0111)&&elf64(c->descriptor);
 case LC_DIRECTORY:return S_ISDIR(s.st_mode)&&s.st_uid==0&&!(s.st_mode&0022);
 case LC_CHANNEL:{int type=0;
   socklen_t size=sizeof type;
   struct sockaddr_storage peer;
   socklen_t plen=sizeof peer;
  return S_ISSOCK(s.st_mode)&&getsockopt(c->descriptor,SOL_SOCKET,SO_TYPE,&type,&size)==0&&type==SOCK_STREAM&&getpeername(c->descriptor,(struct sockaddr *)&peer,&plen)==0&&peer.ss_family==AF_UNIX;
   }
 case LC_NAMESPACE_KEEPER:{char path[64],link[128];
   ssize_t n;
  if(snprintf(path,sizeof path,"/proc/self/fd/%d",c->descriptor)<0)return 0;
  n=readlink(path,link,sizeof link-1);
   if(n<0||n>=(ssize_t)sizeof link-1)return 0;
   link[n]=0;
  return strcmp(link,"anon_inode:[pidfd]")==0&&pidfd_alive(c->descriptor);
   }
 }
 return 0;
}
static void dispose_storage(lc_adapter *a) {
 size_t i;
 if(!a)return;
 if(a->requester_pidfd>=0)close(a->requester_pidfd);
 for(i=0;i<a->operation.handle_count;i++)if(a->caps[i].descriptor>=0)close(a->caps[i].descriptor);
 for(i=0;i<a->operation.command_count;i++){
  struct lc_owned *o=&a->owned[i];
  int fds[]={o->pidfd,o->events,o->kill,o->out,o->err,o->exec_error};
  for(size_t j=0;j<sizeof fds/sizeof fds[0];j++)if(fds[j]>=0)close(fds[j]);
  for(unsigned j=0;j<o->orphan_count;j++)if(o->orphan_pidfds[j]>=0)close(o->orphan_pidfds[j]);
  free(a->argv[i]);
  free(a->envp[i]);
  free(o->out_bytes);
  free(o->err_bytes);
 }
 for(unsigned i=0;i<2;i++) {
  if(a->root_parent[i]>=0)close(a->root_parent[i]);
  if(a->root_directory[i]>=0)close(a->root_directory[i]);
  if(a->bundle_directory[i]>=0)close(a->bundle_directory[i]);
 }
 lr_free_recipe(a->root_recipe);
 free(a->reply);
 free(a->strings);
 free(a);
}
static int root_join(cc_view parent,cc_view name,cc_view full) {
 if(!string(parent)||parent.size<2||parent.data[0]!='/'||parent.data[parent.size-1]=='/'||
  !string(name)||!name.size||memchr(name.data,'/',name.size)||literal(name,".")||literal(name,".."))return 0;
 if(parent.size>LC_STRING_MAX-name.size-1||full.size!=parent.size+name.size+1)return 0;
 return !memcmp(full.data,parent.data,parent.size)&&full.data[parent.size]=='/'&&!memcmp(full.data+parent.size+1,name.data,name.size);
}
static int root_binding_shape(const lc_operation *o,const lr_binding *b,const char **failure) {
 if(b->version!=LR_VERSION||b->size!=sizeof *b||b->engine_profile!=LR_MAPPED_UID0_ENGINE||o->identity_class!=LC_PRODUCTION||
  !lr_validate(b->recipe,failure)||!same(o->expected_input_sha256,b->recipe->expected_input_sha256)||!same(o->custody_identity,b->recipe->custody_identity)||
  o->maximum_commands!=b->recipe->maximum_commands||
  !root_join(b->work_parent_path,b->work_name,b->recipe->work_root)||!root_join(b->evidence_parent_path,b->evidence_name,b->recipe->evidence_root))return refuse(failure,"root-binding-correspondence");
 const lc_capability *parents[]={&b->work_parent,&b->evidence_parent};
 for(unsigned i=0;i<2;i++) {
  const lc_capability *c=parents[i];
  if(c->version!=LC_VERSION||c->size!=sizeof *c||c->role!=LC_DIRECTORY||c->slot!=LC_NONE||c->descriptor<3||c->descriptor>=(int)LC_FD_MAX||c->access!=O_RDONLY||!c->inode||!c->mount_identity)return refuse(failure,"root-parent-layout");
 }
 if(same(b->recipe->work_root,b->recipe->evidence_root)||
  (b->recipe->work_root.size<b->recipe->evidence_root.size&&
   !memcmp(b->recipe->work_root.data,b->recipe->evidence_root.data,b->recipe->work_root.size)&&b->recipe->evidence_root.data[b->recipe->work_root.size]=='/')||
  (b->recipe->evidence_root.size<b->recipe->work_root.size&&
   !memcmp(b->recipe->evidence_root.data,b->recipe->work_root.data,b->recipe->evidence_root.size)&&b->recipe->work_root.data[b->recipe->evidence_root.size]=='/'))return refuse(failure,"root-roots-overlap");
 for(size_t i=0;i<o->command_count;i++) {
  const lc_command_slot *slot=&o->command_slots[i];const lc_capability *c=&o->handles[slot->directory_handle];
  if(slot->argument_count||slot->environment_count||!same(slot->working_directory,b->work_parent_path)||c->device!=b->work_parent.device||c->inode!=b->work_parent.inode||c->mount_identity!=b->work_parent.mount_identity)return refuse(failure,"root-slot-pool-anchor");
 }
 return 1;
}
static int root_open_beneath(int parent,const char *path,int flags) {
 struct open_how how={.flags=(uint64_t)(flags|O_CLOEXEC),.resolve=RESOLVE_BENEATH|RESOLVE_NO_SYMLINKS|RESOLVE_NO_MAGICLINKS|RESOLVE_NO_XDEV};
 return (int)syscall(SYS_openat2,parent,path,&how,sizeof how);
}
static int root_same_directory(int fd,int original,uint32_t uid,uint32_t gid) {
 struct stat a,b;struct statx x,y;
 return fstat(fd,&a)==0&&S_ISDIR(a.st_mode)&&a.st_uid==uid&&a.st_gid==gid&&(a.st_mode&0777)==0700&&
  (original<0||(fstat(original,&b)==0&&a.st_dev==b.st_dev&&a.st_ino==b.st_ino&&
   statx(fd,"",AT_EMPTY_PATH,STATX_MNT_ID,&x)==0&&statx(original,"",AT_EMPTY_PATH,STATX_MNT_ID,&y)==0&&
   (x.stx_mask&STATX_MNT_ID)&&(y.stx_mask&STATX_MNT_ID)&&x.stx_mnt_id==y.stx_mnt_id));
}
static int root_refresh(lc_adapter *a,int discover) {
 const cc_view names[]={a->root_binding.work_name,a->root_binding.evidence_name};
 for(unsigned i=0;i<2;i++) {
  int fd=root_open_beneath(a->root_parent[i],(const char *)names[i].data,O_RDONLY|O_DIRECTORY);
  if(fd<0||!root_same_directory(fd,a->root_directory[i],a->root_uid,a->root_gid)){if(fd>=0)close(fd);return fail(a,"root-selected-root-directory-changed");}
  if(a->root_directory[i]<0)a->root_directory[i]=fd;else close(fd);
 }
 if(discover)for(unsigned i=0;i<2;i++) {
  int fd=root_open_beneath(a->root_directory[0],i?"bundle-b":"bundle-a",O_RDONLY|O_DIRECTORY);
  if(fd<0){if(errno==ENOENT)continue;return fail(a,"root-bundle-path-unknown");}
  if(!root_same_directory(fd,a->bundle_directory[i],a->root_uid,a->root_gid)){close(fd);return fail(a,"root-bundle-directory-changed");}
  int owner=root_open_beneath(fd,"owner",O_RDONLY|O_NOFOLLOW);struct stat st;unsigned char nonce[33];
  int ok=owner>=0&&fstat(owner,&st)==0&&S_ISREG(st.st_mode)&&st.st_nlink==1&&st.st_uid==a->root_uid&&st.st_gid==a->root_gid&&!(st.st_mode&0022)&&st.st_size==32&&read(owner,nonce,sizeof nonce)==32&&
   !memcmp(nonce,a->root_state.recipe->bundle_nonce[i].data,32);
  if(owner>=0)close(owner);
  if(!ok||!lr_note_bundle(&a->root_state,i)){close(fd);return fail(a,"root-bundle-owner-or-lifecycle-changed");}
  if(a->bundle_directory[i]<0)a->bundle_directory[i]=fd;else close(fd);
 }
 return 1;
}
static lc_adapter *open_adapter(const lc_operation *o,const lr_binding *binding,cc_workspace workspace,cc_lease *leases,size_t lease_capacity,const char **failure) {
 lc_adapter *a;
 size_t i,j;
 int operation=-1,control=-1,requester=-1;
 uint64_t utc;
 if(!lc_validate(o,failure)||(binding&&!root_binding_shape(o,binding,failure)))return NULL;
 if(!workspace.frame||!workspace.text||!workspace.nodes||!workspace.node_capacity||!leases||
  workspace.frame_capacity!=CC_FRAME_MAX||workspace.text_capacity!=CC_FRAME_MAX||workspace.node_capacity>16384||
  lease_capacity!=o->maximum_commands){refuse(failure,"bounded-caller-workspace");
  return NULL;
  }
 if(binding) {
  uint64_t storage=(uint64_t)sizeof(lc_adapter)+lr_recipe_storage_bound()+LC_STRING_MAX+CC_FRAME_MAX+4u+
   (uint64_t)o->maximum_commands*2u*CC_OUTPUT_MAX+(uint64_t)LC_VECTOR_MAX*3u*sizeof(char *)+
   (uint64_t)o->maximum_commands*6u*sizeof(char *)+workspace.frame_capacity+workspace.text_capacity+
   (uint64_t)workspace.node_capacity*sizeof(cc_node)+(uint64_t)lease_capacity*sizeof(cc_lease);
  if(!binding->reserved_other_bytes||storage>o->memory_max_bytes||binding->reserved_other_bytes>o->memory_max_bytes-storage){refuse(failure,"root-full-retained-memory-reservation");return NULL;}
 }
 a=calloc(1,sizeof *a);
 if(!a){refuse(failure,"adapter-storage");
  return NULL;
  }
 for(unsigned r=0;r<2;r++)a->root_parent[r]=a->root_directory[r]=a->bundle_directory[r]=-1;
 for(unsigned r=0;r<LR_STORE_COUNT;r++)a->root_census_slot[r]=LC_NONE;
 a->operation=*o;
 a->active=LC_NONE;
 a->channel=a->user_ns=a->mount_ns=a->keeper=a->requester_pidfd=-1;
 for(i=0;i<o->handle_count;i++)a->caps[i].descriptor=-1;
 for(i=0;i<o->command_count;i++){a->owned[i].pidfd=a->owned[i].events=a->owned[i].kill=a->owned[i].out=a->owned[i].err=a->owned[i].exec_error=-1;
  }
 a->strings=malloc(LC_STRING_MAX);
 a->reply=malloc(CC_FRAME_MAX+4u);
 if(!a->strings||!a->reply)goto storage;
 a->operation.schema=copy(a,o->schema);
 a->operation.operation_id=copy(a,o->operation_id);
 a->operation.custody_identity=copy(a,o->custody_identity);
 a->operation.admission_sha256=copy(a,o->admission_sha256);
 a->operation.expected_input_sha256=copy(a,o->expected_input_sha256);
 a->operation.source_sha256=copy(a,o->source_sha256);
 a->operation.adapter_binary_sha256=copy(a,o->adapter_binary_sha256);
 a->operation.toolchain_closure_sha256=copy(a,o->toolchain_closure_sha256);
 a->operation.native_profile_sha256=copy(a,o->native_profile_sha256);
 if(binding) {
  a->root_binding=*binding;
  a->root_recipe=lr_copy_recipe(binding->recipe);
  if(!a->root_recipe||!lr_init(&a->root_state,lr_copied_recipe(a->root_recipe),failure))goto storage;
  a->root_binding.recipe=a->root_state.recipe;
  a->root_binding.work_parent_path=copy(a,binding->work_parent_path);
  a->root_binding.evidence_parent_path=copy(a,binding->evidence_parent_path);
  a->root_binding.work_name=copy(a,binding->work_name);
  a->root_binding.evidence_name=copy(a,binding->evidence_name);
  if(!a->root_binding.work_parent_path.data||!a->root_binding.evidence_parent_path.data||!a->root_binding.work_name.data||!a->root_binding.evidence_name.data)goto storage;
  const lc_capability *parents[]={&binding->work_parent,&binding->evidence_parent};
  for(unsigned r=0;r<2;r++) {
   a->root_parent[r]=fcntl(parents[r]->descriptor,F_DUPFD_CLOEXEC,3);
   lc_capability c=*parents[r];c.descriptor=a->root_parent[r];
   if(c.descriptor<0)goto capability;
   const cc_view name=r?a->root_binding.evidence_name:a->root_binding.work_name;
   int present=root_open_beneath(c.descriptor,(const char *)name.data,O_PATH|O_NOFOLLOW);
   if(present>=0){close(present);goto capability;}
   if(errno!=ENOENT)goto capability;
  }
 }
 for(i=0;i<o->handle_count;i++) {
  a->caps[i]=o->handles[i];
  a->caps[i].descriptor=fcntl(o->handles[i].descriptor,F_DUPFD_CLOEXEC,3);
  if(a->caps[i].descriptor<0||(!(binding&&a->caps[i].role==LC_DIRECTORY)&&!verify_cap(&a->caps[i])))goto capability;
  switch(a->caps[i].role){case LC_OPERATION_CGROUP:operation=(int)i;
   break;
   case LC_CONTROL_LEAF:control=(int)i;
   break;
   case LC_REQUESTER_LEAF:requester=(int)i;
   break;
   case LC_CHANNEL:a->channel=(int)i;
   break;
   case LC_USER_NAMESPACE:a->user_ns=(int)i;
   break;
   case LC_MOUNT_NAMESPACE:a->mount_ns=(int)i;
   break;
   case LC_NAMESPACE_KEEPER:a->keeper=(int)i;
   break;
   default:break;
   }
 }
 if(operation<0||control<0||requester<0||a->channel<0||a->user_ns<0||a->mount_ns<0||a->keeper<0)goto capability;
 if(ancestor(a->caps[control].descriptor,a->caps[requester].descriptor)||ancestor(a->caps[requester].descriptor,a->caps[control].descriptor))goto capability;
 if(!ancestor(a->caps[control].descriptor,a->caps[operation].descriptor)||!ancestor(a->caps[requester].descriptor,a->caps[operation].descriptor)||
  !number_equal(a->caps[operation].descriptor,"pids.max",32)||!number_equal(a->caps[operation].descriptor,"memory.max",o->memory_max_bytes))goto capability;
 {char expected[128];
  if(snprintf(expected,sizeof expected,"%llu %llu\n",(unsigned long long)o->cpu_max_micros,(unsigned long long)o->cpu_period_micros)<0||!file_equal(a->caps[operation].descriptor,"cpu.max",expected))goto capability;
  }
 a->operation_group=operation;
 a->control_leaf=control;
 if(!namespace_join(a,0)||!membership((long)getpid(),a->caps[control].descriptor))goto capability;
 if(binding) {
  const lc_capability *parents[]={&binding->work_parent,&binding->evidence_parent};
  for(unsigned r=0;r<2;r++){lc_capability c=*parents[r];c.descriptor=a->root_parent[r];if(!verify_root_parent(&c,a->root_uid,a->root_gid))goto capability;}
  for(i=0;i<o->handle_count;i++)if(a->caps[i].role==LC_DIRECTORY&&!verify_root_parent(&a->caps[i],a->root_uid,a->root_gid))goto capability;
 }
 {struct ucred peer;
  socklen_t size=sizeof peer;
  int peerfd;
  if(getsockopt(a->caps[a->channel].descriptor,SOL_SOCKET,SO_PEERCRED,&peer,&size)||size!=sizeof peer||peer.pid<=0)goto capability;
  peerfd=(int)syscall(SYS_pidfd_open,peer.pid,0);
  if(peerfd<0)goto capability;
  int joined=pidfd_alive(peerfd)&&membership(peer.pid,a->caps[requester].descriptor)&&pidfd_alive(peerfd);
  if(!joined){close(peerfd);
   goto capability;
   }
  a->requester_pidfd=peerfd;
  a->requester_diagnostic=peer.pid;
 }
 for(i=0;i<o->command_count;i++) {
  lc_command_slot *s=&a->slots[i];
  const lc_command_slot *source=&o->command_slots[i];
  size_t envstart=a->environment_used,vecstart=a->vector_used;
  int population;
  *s=*source;
  s->command_identity=copy(a,source->command_identity);
  s->executable=copy(a,source->executable);
  s->working_directory=copy(a,source->working_directory);
  if(!s->command_identity.data||!s->executable.data||!s->working_directory.data)goto storage;
  s->arguments=a->vectors+vecstart;
  s->environment=a->environment+envstart;
  a->argv[i]=calloc(s->argument_count+5,sizeof(char *));
  a->envp[i]=calloc(s->environment_count+1,sizeof(char *));
  if(!a->argv[i]||!a->envp[i])goto storage;
  a->argv[i][0]=(char *)"/usr/bin/setsid";
  a->argv[i][1]=(char *)"--wait";
  a->argv[i][2]=(char *)"--";
  a->argv[i][3]=(char *)"/usr/sbin/podman";
  for(j=0;j<s->argument_count;j++){cc_view v=copy(a,source->arguments[j]);
   if(!v.data)goto storage;
   a->vectors[a->vector_used++]=v;
   a->argv[i][j+4]=(char *)v.data;
   }
  for(j=0;j<s->environment_count;j++) {
   lc_environment *e=&a->environment[a->environment_used++];
   size_t n=source->environment[j].name.size,v=source->environment[j].value.size;
   e->name=copy(a,source->environment[j].name);
   e->value=copy(a,source->environment[j].value);
   if(!e->name.data||!e->value.data||a->string_used>LC_STRING_MAX-n-v-2)goto storage;
   a->envp[i][j]=a->strings+a->string_used;
   memcpy(a->envp[i][j],e->name.data,n);
   a->envp[i][j][n]='=';
   memcpy(a->envp[i][j]+n+1,e->value.data,v);
   a->envp[i][j][n+v+1]=0;
   a->string_used+=n+v+2;
  }
  int group=a->caps[s->cgroup_handle].descriptor;
  if(ancestor(group,a->caps[control].descriptor)||ancestor(a->caps[control].descriptor,group)||
   ancestor(group,a->caps[requester].descriptor)||ancestor(a->caps[requester].descriptor,group))goto capability;
  for(size_t prior=0;prior<i;prior++)if(ancestor(group,a->caps[a->slots[prior].cgroup_handle].descriptor)||ancestor(a->caps[a->slots[prior].cgroup_handle].descriptor,group))goto capability;
  if(!ancestor(a->caps[s->cgroup_handle].descriptor,a->caps[operation].descriptor)||!populated(a->caps[s->cgroup_handle].descriptor,&population)||population)goto capability;
  if(!binding) {
   a->owned[i].out_bytes=malloc(CC_OUTPUT_MAX);a->owned[i].err_bytes=malloc(CC_OUTPUT_MAX);
   if(!a->owned[i].out_bytes||!a->owned[i].err_bytes)goto storage;
  }
  a->owned[i].events=openat(a->caps[s->cgroup_handle].descriptor,"cgroup.events",O_RDONLY|O_CLOEXEC|O_NOFOLLOW);
  a->owned[i].kill=openat(a->caps[s->cgroup_handle].descriptor,"cgroup.kill",O_WRONLY|O_CLOEXEC|O_NOFOLLOW);
  if(a->owned[i].events<0||a->owned[i].kill<0)goto capability;
 }
 a->operation.command_slots=a->slots;
 a->operation.handles=a->caps;
 if(!namespace_join(a,binding?0:1))goto capability;
 if(!alive(a,0,&utc)||!cc_init(&a->core,workspace,leases,lease_capacity,a->operation.custody_identity,a->operation.admission_sha256,a->operation.expected_input_sha256,utc,o->deadline_utc_ms,o->aggregate_output_bytes))goto capability;
 if(prctl(PR_GET_CHILD_SUBREAPER,&a->old_subreaper)||prctl(PR_SET_CHILD_SUBREAPER,1))goto capability;
 a->subreaper_changed=1;
 if(failure)*failure=NULL;
 return a;
 storage:refuse(failure,"immutable-copy-storage");
 goto failed;
 capability:refuse(failure,"actual-capability-protection-or-deadline");
 failed:dispose_storage(a);
 return NULL;
}
lc_adapter *lc_open(const lc_operation *o,cc_workspace w,cc_lease *l,size_t n,const char **f) {
 return open_adapter(o,NULL,w,l,n,f);
}
lc_adapter *lc_open_root(const lc_operation *o,const lr_binding *b,cc_workspace w,cc_lease *l,size_t n,const char **f) {
 if(!b){refuse(f,"missing-root-binding");return NULL;}
 return open_adapter(o,b,w,l,n,f);
}
static int command_match(lc_adapter *a,const lc_command_slot *s) {
 const cc_core *c=&a->core;
 size_t command=c->request.command,i,node,n=0;
 const cc_node *x=cc_get_node(c,cc_field(c,command,"Executable"));
 if(!x||!same(x->text,s->executable))return 0;
 x=cc_get_node(c,cc_field(c,command,"WorkingDirectory"));
 if(!x||!same(x->text,s->working_directory))return 0;
 x=cc_get_node(c,cc_field(c,command,"OutputLimit"));
 if(!x||x->integer!=s->output_limit)return 0;
 x=cc_get_node(c,cc_field(c,command,"Arguments"));
 if(!x)return 0;
 node=x->child;
 for(i=0;i<s->argument_count;i++){x=cc_get_node(c,node);
  if(!x||!same(x->text,s->arguments[i]))return 0;
  node=x->next;
  }if(node!=CC_NONE)return 0;
 x=cc_get_node(c,cc_field(c,command,"Environment"));
 if(!x)return 0;
 node=x->child;
 while(node!=CC_NONE){x=cc_get_node(c,node);
  if(!x)return 0;
  for(i=0;i<s->environment_count;i++)if(same(x->key,s->environment[i].name)&&same(x->text,s->environment[i].value))break;
  if(i==s->environment_count)return 0;
  n++;
  node=x->next;
  }
 return n==s->environment_count;
}
static int root_slot_command(lc_adapter *a,uint32_t slot,const lr_selection *selection,int retain) {
 const lr_command *c=selection->command;lc_command_slot candidate=a->slots[slot];
 cc_view arguments[LC_VECTOR_MAX];
 if(c->argument_count>LC_VECTOR_MAX-(selection->removal_id.size?1u:0u))return 0;
 memcpy(arguments,c->arguments,c->argument_count*sizeof *arguments);
 candidate.executable=c->executable;candidate.working_directory=c->working_directory;
 candidate.argument_count=c->argument_count;candidate.arguments=arguments;candidate.environment=c->environment;candidate.environment_count=c->environment_count;candidate.output_limit=c->output_limit;
 if(selection->removal_id.size)arguments[candidate.argument_count++]=selection->removal_id;
 if(!command_match(a,&candidate))return 0;
 if(!retain)return 1;
 a->owned[slot].out_bytes=malloc(CC_OUTPUT_MAX);a->owned[slot].err_bytes=malloc(CC_OUTPUT_MAX);
 if(!a->owned[slot].out_bytes||!a->owned[slot].err_bytes)return fail(a,"root-selected-output-storage");
 if(candidate.argument_count>LC_VECTOR_MAX-a->vector_used||candidate.environment_count>LC_VECTOR_MAX-a->environment_used)return fail(a,"root-selected-vector-bound");
 lc_command_slot *s=&a->slots[slot];
 free(a->argv[slot]);free(a->envp[slot]);
 a->argv[slot]=calloc(candidate.argument_count+5,sizeof(char *));a->envp[slot]=calloc(candidate.environment_count+1,sizeof(char *));
 if(!a->argv[slot]||!a->envp[slot])return fail(a,"root-selected-exec-storage");
 s->executable=copy(a,candidate.executable);s->working_directory=copy(a,candidate.working_directory);
 s->arguments=a->vectors+a->vector_used;s->argument_count=candidate.argument_count;s->environment=a->environment+a->environment_used;s->environment_count=candidate.environment_count;
 if(!s->executable.data||!s->working_directory.data)return fail(a,"root-selected-string-bound");
 a->argv[slot][0]=(char *)"/usr/bin/setsid";a->argv[slot][1]=(char *)"--wait";a->argv[slot][2]=(char *)"--";a->argv[slot][3]=(char *)"/usr/sbin/podman";
 for(size_t i=0;i<s->argument_count;i++){cc_view v=copy(a,arguments[i]);if(!v.data)return fail(a,"root-selected-argument-bound");a->vectors[a->vector_used++]=v;a->argv[slot][i+4]=(char *)v.data;}
 for(size_t i=0;i<s->environment_count;i++) {
  lc_environment *e=&a->environment[a->environment_used++];size_t n=candidate.environment[i].name.size,v=candidate.environment[i].value.size;
  e->name=copy(a,candidate.environment[i].name);e->value=copy(a,candidate.environment[i].value);
  if(!e->name.data||!e->value.data||n>LC_STRING_MAX-v-2||a->string_used>LC_STRING_MAX-n-v-2)return fail(a,"root-selected-environment-bound");
  char *text=a->strings+a->string_used;memcpy(text,e->name.data,n);text[n]='=';memcpy(text+n+1,e->value.data,v);text[n+v+1]=0;
  a->envp[slot][i]=text;a->string_used+=n+v+2;
 }
 unsigned bundle=selection->bundle;
 if(bundle>1||a->bundle_directory[bundle]<0)return fail(a,"root-selected-bundle-not-acquired");
 lc_capability *cap=&a->caps[s->directory_handle];
 int fd=fcntl(a->bundle_directory[bundle],F_DUPFD_CLOEXEC,3);struct stat st;struct statfs fs;struct statx x;
 if(fd<0||fstat(fd,&st)||fstatfs(fd,&fs)||statx(fd,"",AT_EMPTY_PATH,STATX_MNT_ID,&x)||(x.stx_mask&STATX_MNT_ID)==0){if(fd>=0)close(fd);return fail(a,"root-selected-directory-metadata");}
 close(cap->descriptor);cap->descriptor=fd;cap->device=st.st_dev;cap->inode=st.st_ino;cap->mount_identity=x.stx_mnt_id;cap->expected_file_system=fs.f_type;
 a->root_bound[slot]=1;return 1;
}
static int root_prepare_start(lc_adapter *a,uint32_t slot) {
 if(!namespace_join(a,0)||!root_refresh(a,1))return 0;
 lr_state next=a->root_state;lr_selection selected;
 int normal=lr_select(&next,0,&selected)&&root_slot_command(a,slot,&selected,0);
 if(!normal) {
  next=a->root_state;
  if(!lr_select(&next,1,&selected)||!root_slot_command(a,slot,&selected,0))return fail(a,"root-finite-command-mismatch");
 }
 if(selected.removal_id.size) {
  uint32_t origin=a->root_census_slot[next.pending_store];
  if(origin>=slot||!a->root_reported[origin]||!a->owned[origin].settled||a->owned[origin].exit_code||a->owned[origin].signal||!a->owned[origin].out_eof||!a->owned[origin].err_eof)return fail(a,"root-original-retired-census-lease");
 }
 if(!root_slot_command(a,slot,&selected,1))return 0;
 a->root_state=next;return 1;
}
static int root_bundles_absent(lc_adapter *a) {
 const cc_view names[]={a->root_binding.work_name,a->root_binding.evidence_name};
 for(unsigned i=0;i<2;i++) {
  int fd=root_open_beneath(a->root_parent[i],(const char *)names[i].data,O_RDONLY|O_DIRECTORY);
  if(fd<0){if(errno==ENOENT&&a->root_directory[i]<0)continue;return 0;}
  if(!root_same_directory(fd,a->root_directory[i],a->root_uid,a->root_gid)){close(fd);return 0;}
  if(i==0)for(unsigned bundle=0;bundle<2;bundle++) {
   int present=root_open_beneath(fd,bundle?"bundle-b":"bundle-a",O_PATH|O_NOFOLLOW);
   if(present>=0){close(present);close(fd);return 0;}
   if(errno!=ENOENT){close(fd);return 0;}
  }
  close(fd);
 }
 return 1;
}
static int root_eof_terminal(lc_adapter *a) {
 if(!root_bundles_absent(a))return 0;
 if(!a->next_slot&&!a->root_state.pending&&!a->root_state.poisoned) {
  /* No workload was attempted. Absence settles only the abort custody state,
   * never C4/qualification success or a supplying-controller admission. */
  a->root_state.cleanup_started=a->root_state.aborting=a->root_state.terminal=1;
  a->root_state.store=LR_STORE_COUNT;
 }
 return lr_terminal(&a->root_state);
}
/* No libc allocator, CLR callback or caller code in the clone child. All argv,
 * environment and descriptors are copied/prepared before atomic birth. */
static void child(lc_adapter *a,uint32_t slot,int out,int err,int handshake) {
 const lc_command_slot *s=&a->slots[slot];
 int supervisor=a->caps[s->supervisor_handle].descriptor;
 int error=0;
 if(syscall(SYS_setgroups,0,NULL)<0||
  syscall(SYS_setns,a->caps[a->user_ns].descriptor,CLONE_NEWUSER)<0||
  syscall(SYS_setns,a->caps[a->mount_ns].descriptor,CLONE_NEWNS)<0||
  syscall(SYS_setresgid,a->root_recipe?0:LC_NS_UID,a->root_recipe?0:LC_NS_UID,a->root_recipe?0:LC_NS_UID)<0||
  syscall(SYS_setresuid,a->root_recipe?0:LC_NS_UID,a->root_recipe?0:LC_NS_UID,a->root_recipe?0:LC_NS_UID)<0||syscall(SYS_fchdir,a->caps[s->directory_handle].descriptor)<0||
  syscall(SYS_dup2,out,STDOUT_FILENO)<0||syscall(SYS_dup2,err,STDERR_FILENO)<0||
  syscall(SYS_prctl,PR_SET_NO_NEW_PRIVS,1,0,0,0)<0)goto setup_failed;
 /* Close every inherited descriptor except the pinned supervisor, error pipe,
  * stdout/stderr. Stdin is closed; root controls cannot reach the workload. */
 syscall(SYS_close,STDIN_FILENO);
 if(supervisor!=3&&syscall(SYS_dup3,supervisor,3,O_CLOEXEC)<0)goto setup_failed;
 if(handshake!=4&&syscall(SYS_dup3,handshake,4,O_CLOEXEC)<0)goto setup_failed;
 handshake=4;
 if(syscall(SYS_close_range,5u,~0u,0u)<0)goto setup_failed;
 syscall(SYS_execveat,3,"",a->argv[slot],a->envp[slot],AT_EMPTY_PATH);
 setup_failed:error=errno;
 syscall(SYS_write,handshake,&error,sizeof error);
 syscall(SYS_exit,127);
 __builtin_unreachable();
}
static int limits_unchanged(lc_adapter *a) {
 char expected[128];
 int group=a->caps[a->operation_group].descriptor;
 if(snprintf(expected,sizeof expected,"%llu %llu\n",(unsigned long long)a->operation.cpu_max_micros,(unsigned long long)a->operation.cpu_period_micros)<0)return 0;
 return protected_controls(group)&&number_equal(group,"pids.max",32)&&number_equal(group,"memory.max",a->operation.memory_max_bytes)&&file_equal(group,"cpu.max",expected);
}
static int launch(lc_adapter *a,uint32_t slot) {
 int out[2]={-1,-1},err[2]={-1,-1},handshake[2]={-1,-1},pidfd=-1;
 long pid;
 uint64_t utc;
 struct clone_args args;
 struct lc_owned *o=&a->owned[slot];
 if(!alive(a,1,&utc))return 0;
 if(!limits_unchanged(a)||!namespace_join(a,1))return fail(a,"pre-birth-capability-join-changed");
 {int pop;
  if(!populated(a->caps[a->slots[slot].cgroup_handle].descriptor,&pop)||pop)return fail(a,"pre-birth-cgroup-not-empty");
  }
 if(pipe2(out,O_CLOEXEC|O_NONBLOCK)||pipe2(err,O_CLOEXEC|O_NONBLOCK)||pipe2(handshake,O_CLOEXEC|O_NONBLOCK))goto failed;
 /* Child writes block; parent reads are nonblocking and pumped continuously. */
 if(fcntl(out[1],F_SETFL,0)||fcntl(err[1],F_SETFL,0))goto failed;
 memset(&args,0,sizeof args);
 args.flags=CLONE_INTO_CGROUP|CLONE_PIDFD;
 args.exit_signal=SIGCHLD;
 args.cgroup=(uint64_t)a->caps[a->slots[slot].cgroup_handle].descriptor;
 args.pidfd=(uint64_t)(uintptr_t)&pidfd;
 pid=syscall(SYS_clone3,&args,sizeof args);
 if(pid==0)child(a,slot,out[1],err[1],handshake[1]);
 if(pid<0)goto failed;
 /* From here the child is owned even if later checks/acknowledgment fail. */
 o->born=1;
 o->pidfd=pidfd;
 o->out=out[0];
 o->err=err[0];
 o->exec_error=handshake[0];
 a->active=slot;
 close(out[1]);
 close(err[1]);
 close(handshake[1]);
 if(pidfd<0)return fail(a,"atomic-birth-pidfd-unknown");
 return 1;
 failed:for(unsigned i=0;i<2;i++){if(out[i]>=0)close(out[i]);
  if(err[i]>=0)close(err[i]);
  if(handshake[i]>=0)close(handshake[i]);
  }
 return fail(a,"atomic-clone-or-pipe-refused");
}
/* Return complete UTF8 prefix; partial trailing sequence remains charged. */
static int utf8_prefix(const unsigned char *p,size_t size,int eof,size_t *prefix) {
 size_t i=0;
 while(i<size) {
  size_t start=i;
  unsigned char b=p[i++];
  unsigned n;
  uint32_t cp,min;
  if(b<128)continue;
  if(b>=0xc2&&b<=0xdf){n=1;
   cp=b&31u;
   min=128;
   }
  else if(b>=0xe0&&b<=0xef){n=2;
   cp=b&15u;
   min=2048;
   }
  else if(b>=0xf0&&b<=0xf4){n=3;
   cp=b&7u;
   min=65536;
   }else return 0;
  if(n>size-i){for(size_t k=i;k<size;k++)if((p[k]&0xc0u)!=0x80u)return 0;
   if(eof)return 0;
   *prefix=start;
   return 1;
   }
  while(n--){b=p[i++];
   if((b&0xc0u)!=0x80u)return 0;
   cp=(cp<<6)|(b&63u);
   }
  if(cp<min||cp>0x10ffffu||(cp>=0xd800u&&cp<=0xdfffu))return 0;
 }
 *prefix=size;
 return 1;
}
static int reserve_ok(lc_adapter *,uint64_t);
static int capture(lc_adapter *a,struct lc_owned *o,int which) {
 int *fd=which?&o->err:&o->out;
 int *eof=which?&o->err_eof:&o->out_eof;
 size_t *size=which?&o->err_size:&o->out_size;
 unsigned char *bytes=which?o->err_bytes:o->out_bytes;
 unsigned char scratch[4096];
 unsigned reads=0;
 size_t prefix;
 while(*fd>=0&&reads++<16) {
  ssize_t n=read(*fd,scratch,sizeof scratch);
  if(n==0){close(*fd);
   *fd=-1;
   *eof=1;
   if(!a->failure&&!utf8_prefix(bytes,*size,1,&prefix))return fail(a,"capture-invalid-utf8-eof");
   return 1;
   }
  if(n<0){if(errno==EAGAIN||errno==EWOULDBLOCK)return 1;
   if(errno==EINTR)continue;
   return fail(a,"capture-read-unknown");
   }
  uint64_t *stream_captured=which?&o->stderr_captured:&o->stdout_captured;
  if(*stream_captured>UINT64_MAX-(uint64_t)n)return fail(a,"capture-diagnostic-overflow");
  *stream_captured+=(uint64_t)n;
  /* Failure cleanup drains/discards without ever producing an acknowledgment.
   * Retain first cause and original diagnostic count; no truncation-success. */
  if(a->failure){if(o->captured>UINT64_MAX-(uint64_t)n)return 0;
   o->captured+=(uint64_t)n;
   continue;
   }
  if(!reserve_ok(a,0)||(uint64_t)n>CC_OUTPUT_MAX-o->captured||(uint64_t)n>a->operation.aggregate_output_bytes-a->core.charged-a->pending_capture){o->captured+=(uint64_t)n;
   return fail(a,"capture-allowance-exceeded");
   }
  for(ssize_t j=0;j<n;j++)o->escaped+=scratch[j]<32?6u:(scratch[j]=='"'||scratch[j]=='\\'?2u:1u);
  /* Even an empty acknowledgment envelope cannot represent this snapshot.
   * The core also checks its exact envelope before any observation commits. */
  if(o->escaped>CC_FRAME_MAX)return fail(a,"captured-reply-unrepresentable");
  memcpy(bytes+*size,scratch,(size_t)n);
  *size+=(size_t)n;
  o->captured+=(uint64_t)n;
  a->pending_capture+=(uint64_t)n;
  if(!utf8_prefix(bytes,*size,0,&prefix))return fail(a,"capture-invalid-utf8");
 }
 return 1;
}
/* Every retained slot was atomically born under this same root owner. A
 * different serial slot's leader/orphans never belong to the queried lease. */
static int other_retained_command(lc_adapter *a,uint32_t slot,long diagnostic) {
 for(uint32_t i=0;i<a->next_slot;i++)if(i!=slot&&a->owned[i].born){
  struct lc_owned *other=&a->owned[i];long joined;
  if(other->pidfd>=0&&pidfd_number(other->pidfd,&joined)&&joined==diagnostic)return 1;
  for(unsigned k=0;k<other->orphan_count;k++)if(other->orphan_pidfds[k]>=0&&pidfd_number(other->orphan_pidfds[k],&joined)&&joined==diagnostic)return 1;
 }
 return 0;
}
static int pump(lc_adapter *a,uint32_t slot) {
 struct lc_owned *o=&a->owned[slot];
 int population;
 if(!o->born)return 1;
 /* A failed fresh ownership join must not preserve a cached retirement. */
 o->settled=0;
 if(o->pidfd<0)return fail(a,"original-pidfd-unavailable");
 if(!capture(a,o,0)||!capture(a,o,1))return 0;
 if(o->exec_error>=0){int error;
  ssize_t n=read(o->exec_error,&error,sizeof error);
  if(n>0){fail(a,"child-setup-or-exec-refused");
   close(o->exec_error);
   o->exec_error=-1;
   }else if(n==0){close(o->exec_error);
   o->exec_error=-1;
   }else if(errno!=EAGAIN&&errno!=EWOULDBLOCK&&errno!=EINTR)return fail(a,"exec-handshake-unknown");
  }
 if(!o->exit_known) {
  siginfo_t info;
  memset(&info,0,sizeof info);
  if(waitid(P_PIDFD,(id_t)o->pidfd,&info,WEXITED|WNOHANG)<0)return fail(a,"pidfd-leader-wait-unknown");
  if(info.si_pid){o->exit_known=1;
   if(info.si_code==CLD_EXITED)o->exit_code=info.si_status;
   else{o->signal=info.si_status;
    o->exit_code=128+info.si_status;
    }}
 }
 if(!populated(a->caps[a->slots[slot].cgroup_handle].descriptor,&population))return fail(a,"recursive-population-unknown");
 o->empty=!population;
 if(o->exit_known&&population)o->leader_left_descendants=1;
 /* Retain adopted descendants while live and actually joined. A zombie's
  * cgroup attribution need not survive exit: never reconstruct its authority
  * from a post-exit numeric ID. The direct-child list is bounded discovery;
  * P_PIDFD wait independently enforces parent/subreaper custody. */
 {char path[128],children[1024];
  int proc;
  unsigned seen=0;
  if(snprintf(path,sizeof path,"/proc/self/task/%ld",(long)getpid())<0)return fail(a,"subreaper-path");
  proc=open(path,O_RDONLY|O_DIRECTORY|O_CLOEXEC|O_NOFOLLOW);
  if(proc<0)return fail(a,"subreaper-directory");
  int read_ok=read_at(proc,"children",children,sizeof children);
  close(proc);
  if(!read_ok)return fail(a,"subreaper-children-observation");
  char *cursor=children;
  while(*cursor){char *end;
   errno=0;
   long diagnostic=strtol(cursor,&end,10);
   unsigned known;
   if(end==cursor||errno||diagnostic<=0||seen++>=32)return fail(a,"subreaper-child-budget");
   cursor=end;
   while(*cursor==' '||*cursor=='\n')cursor++;
   /* The requester is retained from SO_PEERCRED + its original pidfd; its
    * zombie belongs to the controller, not the command or this reaper. */
   long joined_diagnostic;
   if(pidfd_number(o->pidfd,&joined_diagnostic)&&joined_diagnostic==diagnostic)continue;
   if(diagnostic==a->requester_diagnostic&&pidfd_number(a->requester_pidfd,&joined_diagnostic)&&joined_diagnostic==diagnostic)continue;
   if(other_retained_command(a,slot,diagnostic))continue;
   for(known=0;known<o->orphan_count;known++)if(o->orphan_pidfds[known]>=0&&o->orphan_diagnostics[known]==diagnostic)break;
   if(known<o->orphan_count)continue;
   int orphan=(int)syscall(SYS_pidfd_open,diagnostic,0);
   if(orphan<0)return fail(a,"child-pidfd-observation");
   if(pidfd_alive(orphan)&&membership(diagnostic,a->caps[a->slots[slot].cgroup_handle].descriptor)&&pidfd_alive(orphan)){
    if(o->orphan_count>=32){close(orphan);
     return fail(a,"owned-orphan-budget");
     }
    o->orphan_diagnostics[o->orphan_count]=diagnostic;
    o->orphan_pidfds[o->orphan_count++]=orphan;
   }else if(pidfd_alive(orphan)&&membership(diagnostic,a->caps[a->control_leaf].descriptor)&&pidfd_alive(orphan))close(orphan);
   else {close(orphan);
    return fail(a,"unjoined-child-original-custody-unknown");
    }
  }
  for(unsigned i=0;i<o->orphan_count;i++)if(o->orphan_pidfds[i]>=0){siginfo_t info;
   memset(&info,0,sizeof info);
   if(waitid(P_PIDFD,(id_t)o->orphan_pidfds[i],&info,WEXITED|WNOHANG)<0)return fail(a,"owned-orphan-parent-wait-unknown");
   if(info.si_pid){close(o->orphan_pidfds[i]);
    o->orphan_pidfds[i]=-1;
    o->orphan_reaped++;
    }
  }
 }
 o->settled=o->exit_known&&o->empty&&o->out_eof&&o->err_eof&&o->exec_error<0&&o->orphan_count==o->orphan_reaped;
 if(a->root_recipe&&o->settled&&!a->root_reported[slot]) {
  unsigned census=a->root_state.pending_kind==1;uint32_t store=a->root_state.pending_store;
  if(!lr_retired(&a->root_state,o->signal?128+o->signal:o->exit_code,(cc_view){o->out_bytes,o->out_size},o->out_eof,o->err_eof))return fail(a,a->root_state.failure?a->root_state.failure:"root-retired-result-refused");
  if(census)a->root_census_slot[store]=slot;
  a->root_reported[slot]=1;
 }
 return 1;
}
static int cancel(lc_adapter *a,uint32_t slot) {
 struct lc_owned *o=&a->owned[slot];
 if(!o->born||o->settled)return 1;
 if(o->pidfd>=0&&syscall(SYS_pidfd_send_signal,o->pidfd,SIGTERM,NULL,0)<0&&errno!=ESRCH)fail(a,"pidfd-signal-unknown");
 if(pwrite(o->kill,"1",1,0)!=1)return fail(a,"recursive-kill-unknown");
 return 1;
}
static int reserve_ok(lc_adapter *a,uint64_t extra) {
 if(a->core.charged>a->operation.aggregate_output_bytes||a->pending_capture>a->operation.aggregate_output_bytes-a->core.charged||extra>a->operation.aggregate_output_bytes-a->core.charged-a->pending_capture)return fail(a,"shared-pending-retain-allowance");
 return 1;
}
static int perform(lc_adapter *a,size_t *reply_size) {
 cc_result result;
 uint64_t utc;
 uint32_t slot=LC_NONE;
 struct lc_owned *o=NULL;
 uint64_t transfer=0;
 if(!alive(a,0,&utc)||!reserve_ok(a,0))return 0;
 memset(&result,0,sizeof result);
 result.accepted=1;
 switch(a->core.request.action) {
 case CC_BIND_INPUT:break;
 case CC_RETAIN:if(!reserve_ok(a,a->core.request.retained_bytes))return 0;
  break;
 case CC_START:
  if(a->next_slot>=a->operation.maximum_commands||(a->active!=LC_NONE&&!a->owned[a->active].settled))return fail(a,"no-unused-serial-command-slot");
  slot=a->next_slot++;
  /* Consume before matching/refusal/attempted birth. */
  if(a->root_recipe&&!root_prepare_start(a,slot))return 0;
  if(!command_match(a,&a->slots[slot]))return fail(a,"exact-root-command-mismatch");
  if(!launch(a,slot))return 0;
  result.lease=a->slots[slot].command_identity;
  break;
 case CC_OBSERVE:case CC_CANCEL:
  for(slot=0;slot<a->next_slot;slot++)if(same(a->slots[slot].command_identity,a->core.request.lease))break;
  if(slot>=a->next_slot||a->owned[slot].pidfd<0)return fail(a,"original-command-lease");
  if(a->core.request.action==CC_CANCEL&&!cancel(a,slot))return 0;
  if(!pump(a,slot))return 0;
  o=&a->owned[slot];
  result.lease=a->slots[slot].command_identity;
  result.retired=o->settled;
  result.exit_code=o->exit_code;
  {size_t out_prefix,err_prefix;
   if(!utf8_prefix(o->out_bytes,o->out_size,o->out_eof,&out_prefix)||!utf8_prefix(o->err_bytes,o->err_size,o->err_eof,&err_prefix))return fail(a,"observation-invalid-utf8");
   result.stdout_bytes=(cc_view){o->out_bytes,out_prefix};
   result.stderr_bytes=(cc_view){o->err_bytes,err_prefix};
   transfer=(uint64_t)out_prefix+(uint64_t)err_prefix-o->acknowledged;
  }break;
 }
 if(!alive(a,0,&utc)||!cc_complete(&a->core,&result,utc,a->reply,CC_FRAME_MAX+4u,reply_size))return fail(a,a->core.failure?a->core.failure:"core-result-refused");
 if(o){a->pending_capture-=transfer;
  o->acknowledged+=transfer;
  }
 return 1;
}
static int cleanup(lc_adapter *a) {
 uint64_t utc,boot;
 int settled=0;
 for(uint32_t i=0;i<a->next_slot;i++)if(a->owned[i].born&&!a->owned[i].settled)cancel(a,i);
 while(clocks(&utc,&boot)&&utc<a->operation.deadline_utc_ms&&boot<a->operation.deadline_boot_ns) {
  settled=1;
  for(uint32_t i=0;i<a->next_slot;i++)if(a->owned[i].born){pump(a,i);
   if(!a->owned[i].settled)settled=0;
   }
  if(settled)break;
  {struct pollfd p={-1,0,0};
   if(poll(&p,0,10)<0&&errno!=EINTR)break;
   }
 }
 a->terminal=settled;
 return settled;
}
int lc_serve(lc_adapter *a) {
 unsigned char bytes[4096];
 size_t reply_size=0,reply_sent=0;
 uint64_t utc,boot;
 if(!a||a->served)return 0;
 a->served=1;
 if(fcntl(a->caps[a->channel].descriptor,F_SETFL,O_NONBLOCK)<0)return fail(a,"channel-nonblocking");
 while(!a->failure) {
  struct pollfd fds[5];
  size_t count=1;
  int status;
  if(!alive(a,0,&utc))break;
  if(!clocks(&utc,&boot)||a->operation.deadline_boot_ns-boot<=a->operation.cleanup_reserve_ns){fail(a,"operation-cleanup-reserve-reached");
   break;
   }
  if(!limits_unchanged(a)||!pidfd_alive(a->caps[a->keeper].descriptor)){fail(a,"live-control-or-namespace-keeper-lost");
   break;
   }
  if(a->active!=LC_NONE&&!pump(a,a->active))break;
  fds[0]=(struct pollfd){a->caps[a->channel].descriptor,reply_size?(POLLOUT|POLLIN):POLLIN,0};
  if(a->active!=LC_NONE){struct lc_owned *o=&a->owned[a->active];
   if(o->out>=0)fds[count++]=(struct pollfd){o->out,POLLIN,0};
   if(o->err>=0)fds[count++]=(struct pollfd){o->err,POLLIN,0};
   if(!o->exit_known)fds[count++]=(struct pollfd){o->pidfd,POLLIN,0};
   fds[count++]=(struct pollfd){o->events,POLLPRI|POLLERR,0};
   }
  status=poll(fds,(nfds_t)count,10);
  if(status<0){if(errno==EINTR)continue;
   fail(a,"transport-poll-unknown");
   break;
   }
  if(!alive(a,0,&utc))break;
  if(fds[0].revents&(POLLERR|POLLNVAL)){fail(a,"channel-lost");
   break;
   }
  if(reply_size&&(fds[0].revents&POLLIN)){unsigned char extra;
   ssize_t queued=recv(a->caps[a->channel].descriptor,&extra,1,MSG_PEEK|MSG_DONTWAIT);
   fail(a,queued==0?"acknowledgment-delivery-unknown":"request-before-acknowledgment");
   break;
   }
  if(reply_size&&(fds[0].revents&(POLLOUT|POLLHUP))) {
   ssize_t n=send(a->caps[a->channel].descriptor,a->reply+reply_sent,reply_size-reply_sent,MSG_NOSIGNAL);
   if(n<=0){if(n<0&&(errno==EAGAIN||errno==EINTR))continue;
    fail(a,"acknowledgment-delivery-unknown");
    break;
    }
   if((size_t)n<reply_size-reply_sent)a->partial_acknowledgment=1;
   reply_sent+=(size_t)n;
   if(reply_sent==reply_size)reply_size=reply_sent=0;
  } else if(!reply_size&&(fds[0].revents&(POLLIN|POLLHUP))) {
   size_t remaining=a->core.header_used<4?4-a->core.header_used:a->core.frame_size-a->core.frame_used;
   if(remaining>sizeof bytes)remaining=sizeof bytes;
   ssize_t n=recv(a->caps[a->channel].descriptor,bytes,remaining,0);
   if(n<=0){if(n<0&&(errno==EAGAIN||errno==EINTR))continue;
    int command_terminal=a->root_recipe?root_eof_terminal(a):a->next_slot==a->operation.maximum_commands;
    int complete=n==0&&command_terminal&&!a->core.pending&&!a->core.header_used&&!a->core.frame_used;
    for(uint32_t i=0;i<a->next_slot;i++)if(!a->owned[i].born||!a->owned[i].settled)complete=0;
    if(complete){a->terminal=1;
     break;
     }
    fail(a,"request-disconnect-or-loss");
    break;
   }
   cc_progress progress=cc_feed(&a->core,bytes,(size_t)n,utc);
   if(progress==CC_FAILED){fail(a,a->core.failure);
    break;
    }
   if(progress==CC_READY){unsigned char extra;
    ssize_t queued=recv(a->caps[a->channel].descriptor,&extra,1,MSG_PEEK|MSG_DONTWAIT);
    if(queued>0){fail(a,"pipelined-request-surplus");
     break;
     }
    if(queued==0||(queued<0&&errno!=EAGAIN&&errno!=EWOULDBLOCK&&errno!=EINTR)){fail(a,"request-delivery-loss");
     break;
     }
    if(!perform(a,&reply_size))break;
   }
  }
 }
 int settled=cleanup(a);
 return settled&&!a->failure;
 /* Only kernel-derived completed commands and clean frame EOF. */
}
int lc_root_custody_outcome(lc_adapter *a,int *aborted) {
 if(!a||!aborted||!a->root_recipe||!a->served||!a->terminal||a->failure||!lr_terminal(&a->root_state)||!root_bundles_absent(a))return 0;
 *aborted=a->root_state.aborting!=0;return 1;
}
const char *lc_failure(const lc_adapter *a){return a?a->failure:"missing-adapter";
 }
int lc_observe_original(const lc_adapter *a,uint32_t slot,lc_observation *r) {
 const struct lc_owned *o;
 if(!a||!r||slot>=a->next_slot)return 0;
 o=&a->owned[slot];
 memset(r,0,sizeof *r);
 memcpy(r->operation_id,a->operation.operation_id.data,64);
 memcpy(r->command_identity,a->slots[slot].command_identity.data,64);
 r->slot=slot;
 r->retained_pidfd=o->pidfd;
 r->command_cgroup_descriptor=a->caps[a->slots[slot].cgroup_handle].descriptor;
 r->leader_exit_known=o->exit_known;
 r->leader_exit_code=o->exit_code;
 r->leader_signal=o->signal;
 r->recursive_empty=o->empty;
 r->stdout_eof=o->out_eof;
 r->stderr_eof=o->err_eof;
 r->stdout_captured=o->stdout_captured;
 r->stderr_captured=o->stderr_captured;
 r->cleanup_settled=o->settled;
 r->live_admission=!a->failure;
 return 1;
}
int lc_release(lc_adapter *a) {
 if(!a)return 1;
 for(uint32_t i=0;i<a->next_slot;i++)if(a->owned[i].born&&!a->owned[i].settled)return 0;
 if(a->root_recipe) {
  int born=0;for(uint32_t i=0;i<a->next_slot;i++)if(a->owned[i].born)born=1;
  if(!root_bundles_absent(a)||(born&&!lr_terminal(&a->root_state)))return fail(a,"root-owned-directory-or-program-retirement-unknown");
 }
 if(a->subreaper_changed&&prctl(PR_SET_CHILD_SUBREAPER,a->old_subreaper))return fail(a,"subreaper-restore-unknown");
 dispose_storage(a);
 return 1;
}
