#define _GNU_SOURCE
#include "../../deployment/telemetry-collector/persistent/v3/image-closure/native/custody_adapter.c"
/* Include the production implementation in this focused translation unit to
 * exercise its actual private match/capture/accounting guards. No replacement
 * syscalls, return-value mocks or arbitrary retirement callback is supplied. */
#include <errno.h>
#include <fcntl.h>
#include <linux/magic.h>
#include <linux/sched.h>
#include <poll.h>
#include <sched.h>
#include <signal.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/mount.h>
#include <sys/prctl.h>
#include <sys/socket.h>
#include <sys/stat.h>
#include <sys/statfs.h>
#include <sys/syscall.h>
#include <sys/types.h>
#include <sys/un.h>
#include <sys/wait.h>
#include <time.h>
#include <unistd.h>

/* Fixed test controller/workload only. Shape fixtures below carry no kernel
 * capability and never call open/serve or certify native custody. */
static const unsigned char hex[]="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
static cc_view view(const char *s){return (cc_view){(const unsigned char *)s,strlen(s)};
 }
static cc_view sha(void){return (cc_view){hex,64};
 }
struct shape { lc_operation o;
 lc_command_slot slots[2];
 lc_capability caps[15];
 cc_view args[2];
 lc_environment env[2];
 };
static void shape(struct shape *f) {
 memset(f,0,sizeof *f);
 f->o=(lc_operation){.version=1,.size=sizeof(lc_operation),.schema=view(LC_SCHEMA),.operation_id=sha(),.custody_identity=sha(),
  .admission_sha256=sha(),.expected_input_sha256=sha(),.identity_class=LC_CONTROLLED_NATIVE,.source_sha256=sha(),
  .adapter_binary_sha256=sha(),.toolchain_closure_sha256=sha(),.native_profile_sha256=sha(),.admitted_utc_ms=1000,.deadline_utc_ms=121000,
  .admitted_boot_ns=1000000000,.deadline_boot_ns=121000000000,.cleanup_reserve_ns=15000000000,.aggregate_output_bytes=2097152,
  .memory_max_bytes=536870912,.cpu_max_micros=100000,.cpu_period_micros=100000,.max_owned_tasks=32,.max_active_commands=1,
  .maximum_commands=1,.command_slots=f->slots,.command_count=1,.handles=f->caps,.handle_count=11};
 for(unsigned i=0;i<11;i++)f->caps[i]=(lc_capability){.version=1,.size=sizeof(lc_capability),.role=(lc_role)(i+1),
  .slot=(i==3||i==6||i==7||i==8)?0:UINT32_MAX,.descriptor=(int)i+3,.expected_file_system=1,.device=2,.inode=i+10,.mount_identity=1,.access=i==9?O_RDWR:O_RDONLY};
 f->args[0]=view("--custody-fixture-workload");
 f->args[1]=view("normal");
 f->env[0]=(lc_environment){view("LC_ALL"),view("C")};
 f->env[1]=(lc_environment){view("PATH"),view("/usr/bin:/bin")};
 f->slots[0]=(lc_command_slot){.version=1,.size=sizeof(lc_command_slot),.slot=0,.command_identity=sha(),.executable=view("/usr/sbin/podman"),
  .arguments=f->args,.argument_count=2,.environment=f->env,.environment_count=2,.working_directory=view("/tmp"),.output_limit=1048576,
  .executable_handle=6,.supervisor_handle=7,.directory_handle=8,.cgroup_handle=3};
}
static unsigned controls,failures;
static void check(int condition,const char *name){controls++;
 if(!condition){failures++;
  fprintf(stderr,"FAIL adapter source control %s\n",name);
  }}
static void rejects(struct shape *f,const char *name){const char *why=NULL;
 check(!lc_validate(&f->o,&why)&&why!=NULL,name);
 }
static void source_channel_controls(void);
int fsgg_custody_adapter_controls(void) {
 struct shape f;
 const char *why=NULL;
 controls=failures=0;
 shape(&f);
 check(lc_validate(&f.o,&why)&&why==NULL,"closed shape only");
 #define REJECT(name,...) do{shape(&f);__VA_ARGS__;rejects(&f,name);}while(0)
 REJECT("version",f.o.version=2);
 REJECT("size",f.o.size--);
 REJECT("schema",f.o.schema=view("other"));
 REJECT("identity uppercase",f.o.operation_id=view("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"));
 REJECT("identity absent",f.o.operation_id=(cc_view){NULL,64});
 REJECT("profile absent",f.o.native_profile_sha256=(cc_view){NULL,0});
 REJECT("class",f.o.identity_class=0);
 REJECT("expired interval",f.o.deadline_utc_ms=f.o.admitted_utc_ms);
 REJECT("renewed clock",f.o.deadline_boot_ns++);
 REJECT("over ninety minutes",f.o.deadline_utc_ms=5401001);
 REJECT("no cleanup reserve",f.o.cleanup_reserve_ns=0);
 REJECT("reserve consumes interval",f.o.cleanup_reserve_ns=120000000000);
 REJECT("tasks unbounded",f.o.max_owned_tasks=33);
 REJECT("parallel commands",f.o.max_active_commands=2);
 REJECT("no output allowance",f.o.aggregate_output_bytes=0);
 REJECT("no memory",f.o.memory_max_bytes=0);
 REJECT("cpu over period",f.o.cpu_max_micros++);
 REJECT("count absent",f.o.maximum_commands=0);
 REJECT("count inconsistent",f.o.command_count=2);
 REJECT("table absent",f.o.command_slots=NULL);
 REJECT("cap version",f.caps[0].version=2);
 REJECT("cap size",f.caps[0].size--);
 REJECT("cap role",f.caps[0].role=0);
 REJECT("cap stdin",f.caps[0].descriptor=0);
 REJECT("cap excessive",f.caps[0].descriptor=65536);
 REJECT("cap inode absent",f.caps[0].inode=0);
 REJECT("cap mount absent",f.caps[0].mount_identity=0);
 REJECT("cap alias",f.caps[1].descriptor=f.caps[0].descriptor);
 REJECT("cgroup alias",f.caps[1].inode=f.caps[0].inode);
 REJECT("cap writable controls",f.caps[0].access=O_RDWR);
 REJECT("cap global slot",f.caps[0].slot=0);
 REJECT("cap duplicate role",f.caps[1].role=LC_OPERATION_CGROUP);
 REJECT("cap command slot",f.caps[3].slot=1);
 REJECT("slot version",f.slots[0].version=2);
 REJECT("slot size",f.slots[0].size--);
 REJECT("slot ordinal",f.slots[0].slot=1);
 REJECT("arbitrary executable",f.slots[0].executable=view("/bin/sh"));
 REJECT("relative cwd",f.slots[0].working_directory=view("tmp"));
 REJECT("null cwd",f.slots[0].working_directory=(cc_view){NULL,4});
 REJECT("output allowance",f.slots[0].output_limit--);
 REJECT("arguments absent",f.slots[0].arguments=NULL);
 REJECT("argument count",f.slots[0].argument_count=4097);
 REJECT("environment absent",f.slots[0].environment=NULL);
 REJECT("environment count",f.slots[0].environment_count=4097);
 REJECT("executable cap join",f.slots[0].executable_handle=8);
 REJECT("supervisor cap join",f.slots[0].supervisor_handle=6);
 REJECT("cwd cap join",f.slots[0].directory_handle=7);
 REJECT("cgroup cap join",f.slots[0].cgroup_handle=2);
 REJECT("argument NUL",f.args[0]=(cc_view){(const unsigned char *)"a\0b",3});
 REJECT("argument invalid UTF8",f.args[0]=(cc_view){(const unsigned char *)"\xc0\x80",2});
 REJECT("environment empty name",f.env[0].name=view(""));
 REJECT("environment equals",f.env[0].name=view("X=Y"));
 REJECT("environment duplicate",f.env[1].name=f.env[0].name);
 REJECT("environment NUL",f.env[0].value=(cc_view){(const unsigned char *)"a\0b",3});
 REJECT("namespace keeper mandatory",f.o.handle_count--);
 #undef REJECT
 shape(&f);
 f.args[0]=view("é");
 check(lc_validate(&f.o,&why),"valid UTF8 exec argument");
 shape(&f);
 f.env[0].value=view("");
 check(lc_validate(&f.o,&why),"empty env value retained");
 shape(&f);
 f.slots[0].argument_count=0;
 f.slots[0].arguments=NULL;
 f.slots[0].environment_count=0;
 f.slots[0].environment=NULL;
 check(lc_validate(&f.o,&why),"empty exact vectors");
 source_channel_controls();
 printf("adapter source controls=%u failures=%u native=NOT_RUN\n",controls,failures);
 return (int)failures;
}

static void source_channel_controls(void) {
 struct shape f;
 lc_adapter *a=calloc(1,sizeof *a);
 cc_lease leases[2];
 unsigned char *frame=malloc(CC_FRAME_MAX),*text=malloc(CC_FRAME_MAX);
 cc_node nodes[128];
 uint64_t utc,boot;
 const char *payloads[]={
  "{\"Executable\":\"/usr/sbin/podman\",\"Arguments\":[\"--custody-fixture-workload\",\"normal\"],\"Environment\":{\"PATH\":\"/usr/bin:/bin\",\"LC_ALL\":\"C\"},\"WorkingDirectory\":\"/tmp\",\"OutputLimit\":1048576}",
  "{\"Executable\":\"/usr/sbin/podman\",\"Arguments\":[\"normal\",\"--custody-fixture-workload\"],\"Environment\":{\"PATH\":\"/usr/bin:/bin\",\"LC_ALL\":\"C\"},\"WorkingDirectory\":\"/tmp\",\"OutputLimit\":1048576}",
  "{\"Executable\":\"/usr/sbin/podman\",\"Arguments\":[\"--custody-fixture-workload\",\"normal\"],\"Environment\":{\"PATH\":\"/usr/bin:/bin\",\"LC_ALL\":\"C\",\"EXTRA\":\"1\"},\"WorkingDirectory\":\"/tmp\",\"OutputLimit\":1048576}",
  "{\"Executable\":\"/usr/sbin/podman\",\"Arguments\":[\"--custody-fixture-workload\",\"normal\"],\"Environment\":{\"PATH\":\"/usr/bin:/bin\",\"LC_ALL\":\"C\"},\"WorkingDirectory\":\"/other\",\"OutputLimit\":1048576}"
 };
 if(!a||!frame||!text){check(0,"source storage");
  free(a);
  free(frame);
  free(text);
  return;
  }
 shape(&f);
 clocks(&utc,&boot);
 f.o.admitted_utc_ms=utc;
 f.o.deadline_utc_ms=utc+120000;
 f.o.admitted_boot_ns=boot;
 f.o.deadline_boot_ns=boot+UINT64_C(120000000000);
 a->operation=f.o;
 a->slots[0]=f.slots[0];
 a->active=LC_NONE;
 a->reply=malloc(CC_FRAME_MAX+4);
 cc_workspace workspace={.frame=frame,.text=text,.frame_capacity=CC_FRAME_MAX,.text_capacity=CC_FRAME_MAX,.nodes=nodes,.node_capacity=128};
 workspace.frame=frame;
 workspace.text=text;
 workspace.frame_capacity=CC_FRAME_MAX;
 workspace.text_capacity=CC_FRAME_MAX;
 workspace.nodes=nodes;
 workspace.node_capacity=128;
 for(unsigned i=0;i<4;i++){
  char body[2048];
  unsigned char wire[2052];
  size_t n;
  check(cc_init(&a->core,workspace,leases,2,sha(),sha(),sha(),utc,utc+120000,2097152),"actual core init");
  a->core.bound=1;
  int count=snprintf(body,sizeof body,"{\"schema\":\"%s\",\"identity\":\"%s\",\"ordinal\":1,\"action\":\"start\",\"payload\":%s}",CC_SCHEMA,hex,payloads[i]);
  n=(size_t)count;
  wire[0]=(unsigned char)(n>>24);
  wire[1]=(unsigned char)(n>>16);
  wire[2]=(unsigned char)(n>>8);
  wire[3]=(unsigned char)n;
  memcpy(wire+4,body,n);
  check(cc_feed(&a->core,wire,n+4,utc)==CC_READY,"actual start parse");
  check(command_match(a,&a->slots[0])==(i==0),"complete ordered command match");
  if(i==1){size_t reply=0;
   a->next_slot=0;
   a->failure=NULL;
   check(!perform(a,&reply)&&a->next_slot==1&&!a->owned[0].born,"mismatch consumes slot before birth");
   }
 }
 a->failure=NULL;
 a->core.poisoned=0;
 a->core.charged=0;
 a->operation.aggregate_output_bytes=8;
 a->pending_capture=0;
 struct lc_owned *o=&a->owned[0];
 o->out_bytes=malloc(CC_OUTPUT_MAX);
 o->err_bytes=malloc(CC_OUTPUT_MAX);
 o->out=o->err=-1;
 int p[2];
 if(!o->out_bytes||!o->err_bytes||pipe2(p,O_CLOEXEC|O_NONBLOCK)){check(0,"capture fixture storage");
  goto done;
  }
 o->out=p[0];
 check(write(p[1],"abcdefgh",8)==8&&capture(a,o,0),"idle actual capture charge");
 check(a->pending_capture==8&&a->core.charged==0,"unacknowledged shared charge");
 check(!reserve_ok(a,1)&&a->failure!=NULL,"retain cannot spend pending charge");
 close(p[1]);
 capture(a,o,0);
 check(o->out_eof,"failed capture fixture drained EOF");
 free(o->out_bytes);
 free(o->err_bytes);
 memset(o,0,sizeof *o);
 o->out_bytes=malloc(CC_OUTPUT_MAX);
 o->err_bytes=malloc(CC_OUTPUT_MAX);
 o->out=o->err=-1;
 /* Failure first cause and incomplete UTF8 are independently meaningful. */
 a->failure=NULL;
 a->core.poisoned=0;
 a->pending_capture=0;
 a->operation.aggregate_output_bytes=2097152;
 if(!o->out_bytes||!o->err_bytes||pipe2(p,O_CLOEXEC|O_NONBLOCK)){check(0,"UTF8 fixture storage");
  goto done;
  }
 o->out=p[0];
 check(write(p[1],"\xc3",1)==1&&capture(a,o,0)&&a->pending_capture==1,"partial UTF8 remains charged");
 close(p[1]);
 check(!capture(a,o,0)&&strcmp(a->failure,"capture-invalid-utf8-eof")==0,"incomplete UTF8 EOF poisons");
 fail(a,"later-cleanup");
 check(strcmp(a->failure,"capture-invalid-utf8-eof")==0,"cleanup preserves first cause");
 free(o->out_bytes);
 free(o->err_bytes);
 memset(o,0,sizeof *o);
 o->out_bytes=malloc(CC_OUTPUT_MAX);
 o->err_bytes=malloc(CC_OUTPUT_MAX);
 o->out=o->err=-1;
 a->failure=NULL;
 a->core.poisoned=0;
 a->pending_capture=0;
 if(!o->out_bytes||!o->err_bytes||pipe2(p,O_CLOEXEC|O_NONBLOCK)){check(0,"combined fixture storage");
  goto done;
  }
 o->out=p[0];
 unsigned char chunk[4096];
 memset(chunk,'x',sizeof chunk);
 int exact=1;
 for(unsigned i=0;i<256;i++)if(write(p[1],chunk,sizeof chunk)!=(ssize_t)sizeof chunk||!capture(a,o,0)){exact=0;
  break;
  }
 check(exact&&o->captured==CC_OUTPUT_MAX&&a->pending_capture==CC_OUTPUT_MAX,"actual exact combined capture allowance");
 close(p[1]);
 capture(a,o,0);
 if(pipe2(p,O_CLOEXEC|O_NONBLOCK)){check(0,"combined stderr fixture");
  goto done;
  }o->err=p[0];
 check(write(p[1],"y",1)==1&&!capture(a,o,1)&&strcmp(a->failure,"capture-allowance-exceeded")==0,"one stderr byte beyond combined bound poisons");
 close(p[1]);
 capture(a,o,1);
 free(o->out_bytes);
 free(o->err_bytes);
 memset(o,0,sizeof *o);
 o->out_bytes=malloc(CC_OUTPUT_MAX);
 o->err_bytes=malloc(CC_OUTPUT_MAX);
 o->out=o->err=-1;
 a->failure=NULL;
 a->core.poisoned=0;
 a->pending_capture=0;
 if(!o->out_bytes||!o->err_bytes||pipe2(p,O_CLOEXEC|O_NONBLOCK)){check(0,"escape fixture storage");
  goto done;
  }o->out=p[0];
 memset(chunk,0,sizeof chunk);
 for(unsigned i=0;i<100&&!a->failure;i++){if(write(p[1],chunk,sizeof chunk)!=(ssize_t)sizeof chunk)break;
  capture(a,o,0);
  }
 check(a->failure&&strcmp(a->failure,"captured-reply-unrepresentable")==0&&o->captured<CC_OUTPUT_MAX,"idle unrepresentable escaped snapshot poisons");
 close(p[1]);
 capture(a,o,0);
 if(!pipe2(p,O_CLOEXEC|O_NONBLOCK)){
  shape(&f);
  f.o.admitted_utc_ms=utc;
  f.o.deadline_utc_ms=utc+120000;
  f.o.admitted_boot_ns=boot;
  f.o.deadline_boot_ns=boot+UINT64_C(120000000000);
  for(unsigned i=0;i<11;i++)f.caps[i].descriptor=1000+(int)i;
  f.caps[0].descriptor=p[0];
  const char *original_failure=NULL;
  lc_adapter *refused=lc_open(&f.o,workspace,leases,1,&original_failure);
  check(!refused&&original_failure&&fcntl(p[0],F_GETFD)>=0&&fcntl(p[1],F_GETFD)>=0,"actual bad-capability refusal preserves caller descriptors");
  if(refused)lc_release(refused);
  close(p[0]);
  close(p[1]);
 }else check(0,"bad capability fixture pipe");
 /* Read-only actual self pidfd exercises cross-slot ownership exclusion:
  * no workload birth, signal, namespace or cgroup mutation is involved. */
 {int self=(int)syscall(SYS_pidfd_open,(long)getpid(),0);
  check(self>=0,"source retained self pidfd available");
  if(self>=0){
   a->next_slot=2;a->owned[1].born=1;a->owned[1].pidfd=self;
   check(other_retained_command(a,0,(long)getpid()),"other slot actual retained leader excluded");
   check(!other_retained_command(a,0,(long)getpid()+1),"numeric mismatch cannot supply other slot custody");
   a->owned[1].born=0;
   check(!other_retained_command(a,0,(long)getpid()),"descriptor without born-slot custody excluded from authority");
   close(self);a->owned[1].pidfd=-1;
  }
 }
 done:if(o->out>=0)close(o->out);
 if(o->err>=0)close(o->err);
 free(o->out_bytes);
 free(o->err_bytes);
 free(a->reply);
 free(a);
 free(frame);
 free(text);
}

#ifndef LC_TEST_LIBRARY
/* Native fixture setup is an effect. Only the separately admitted, protected
 * root test profile may call this entry point. Inherited parent FD is a retained
 * delegation capability, not an env-authenticated path; no /sys takeover/sudo,
 * remount of a host namespace, downloads or host Podman installation occurs. */
static uint64_t fx_deadline;
static int fx_alive(void){uint64_t utc,boot;
 return clocks(&utc,&boot)&&boot<fx_deadline;
 }
static int fx_write(int fd,const char *name,const char *value) {
 int out=openat(fd,name,O_WRONLY|O_CLOEXEC|O_NOFOLLOW);
 size_t n=strlen(value);
 if(out<0)return 0;
 int ok=write(out,value,n)==(ssize_t)n;
 close(out);
 return ok;
}
static int fx_child_group(int parent,const char *name) {
 if(mkdirat(parent,name,0755))return -1;
 return openat(parent,name,O_RDONLY|O_DIRECTORY|O_CLOEXEC|O_NOFOLLOW);
}
static int fx_wait_byte(int fd) {
 while(fx_alive()){struct pollfd p={fd,POLLIN,0};
  if(poll(&p,1,10)>0){char c;
   return read(fd,&c,1)==1&&c=='1';
   }}
 return 0;
}
static void fx_pidfd_kill(int fd){if(fd>=0)syscall(SYS_pidfd_send_signal,fd,SIGKILL,NULL,0);
 }
static int fx_wait_exit(int fd) {
 while(fx_alive()){siginfo_t info;
  memset(&info,0,sizeof info);
  if(waitid(P_PIDFD,(id_t)fd,&info,WEXITED|WNOHANG))return 0;
  if(info.si_pid)return 1;
  struct pollfd p={fd,POLLIN,0};
  poll(&p,1,10);
  }
 return 0;
}
static int fx_cap(lc_capability *c,lc_role role,uint32_t slot,int fd) {
 struct stat s;
 struct statfs fs;
 struct statx x;
 int access=fcntl(fd,F_GETFL);
 if(fd<3||access<0||fstat(fd,&s)||fstatfs(fd,&fs)||statx(fd,"",AT_EMPTY_PATH,STATX_MNT_ID,&x)||(x.stx_mask&STATX_MNT_ID)==0)return 0;
 *c=(lc_capability){.version=1,.size=sizeof *c,.role=role,.slot=slot,.descriptor=fd,.expected_file_system=(uint64_t)fs.f_type,
  .device=(uint64_t)s.st_dev,.inode=(uint64_t)s.st_ino,.mount_identity=x.stx_mnt_id,.access=access&O_ACCMODE};
 return 1;
}
static int fx_random(char value[65]) {
 unsigned char bytes[32];
 static const char h[]="0123456789abcdef";
 if(syscall(SYS_getrandom,bytes,sizeof bytes,0)!=(ssize_t)sizeof bytes)return 0;
 for(unsigned i=0;i<32;i++){value[2*i]=h[bytes[i]>>4];
  value[2*i+1]=h[bytes[i]&15];
  }value[64]=0;
 return 1;
}
static int fx_base64(const char *in,char *out,size_t capacity) {
 static const char b[]="ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
 size_t n=strlen(in),at=0;
 if(4*((n+2)/3)+1>capacity)return 0;
 for(size_t i=0;i<n;i+=3){unsigned a=(unsigned char)in[i],c=i+1<n?(unsigned char)in[i+1]:0,d=i+2<n?(unsigned char)in[i+2]:0;
  out[at++]=b[a>>2];
  out[at++]=b[((a&3)<<4)|(c>>4)];
  out[at++]=i+1<n?b[((c&15)<<2)|(d>>6)]:'=';
  out[at++]=i+2<n?b[d&63]:'=';
  }
 out[at]=0;
 return 1;
}
static long fx_clone(int cgroup,int *pidfd) {
 struct clone_args args;
 memset(&args,0,sizeof args);
 args.flags=CLONE_INTO_CGROUP|CLONE_PIDFD;
 args.cgroup=(uint64_t)cgroup;
 args.pidfd=(uint64_t)(uintptr_t)pidfd;
 args.exit_signal=SIGCHLD;
 return syscall(SYS_clone3,&args,sizeof args);
}
static int fx_readonly_bind(const char *path) {
 return mount(path,path,NULL,MS_BIND,NULL)==0&&mount(NULL,path,NULL,MS_REMOUNT|MS_BIND|MS_RDONLY,NULL)==0;
}
static void fx_keeper(const char *binary,int ready,int mapped,int prepared) {
 /* Parent installs exactly two singleton maps before mount setup. The new
  * mount namespace is owned by this new user namespace, matching setns order. */
 if(unshare(CLONE_NEWUSER)||write(ready,"1",1)!=1||!fx_wait_byte(mapped)||setresgid(0,0,0)||setresuid(0,0,0)||unshare(CLONE_NEWNS))_exit(101);
 /* Seal the parent before adding private children. A later nonrecursive
  * /usr bind would hide selected /usr/sbin and setsid submounts. */
 if(mount(NULL,"/",NULL,MS_REC|MS_PRIVATE,NULL)||!fx_readonly_bind("/usr")||
  mount("tmpfs","/usr/sbin","tmpfs",MS_NOSUID|MS_NODEV,"size=1048576,mode=0755"))_exit(102);
 int target=open("/usr/sbin/podman",O_CREAT|O_EXCL|O_RDONLY|O_CLOEXEC,0555);
 if(target<0)_exit(103);
 close(target);
 if(mount(binary,"/usr/sbin/podman",NULL,MS_BIND,NULL)||!fx_readonly_bind("/usr/sbin/podman")||
  !fx_readonly_bind("/usr/bin/setsid")||
  mount("tmpfs","/sys/fs/cgroup","tmpfs",MS_NOSUID|MS_NODEV|MS_NOEXEC,"size=1048576,mode=0555")||
  mount(NULL,"/sys/fs/cgroup",NULL,MS_REMOUNT|MS_RDONLY|MS_NOSUID|MS_NODEV|MS_NOEXEC,NULL)||write(prepared,"1",1)!=1)_exit(104);
 for(;;)pause();
}
static int fx_workload(const char *kind) {
 if(getuid()!=32768||getgid()!=32768)return 90;
 if(!strcmp(kind,"normal")){if(write(1,"native-output\n",14)!=14||write(2,"native-error\n",13)!=13)return 91;
  return 0;
  }
 if(!strcmp(kind,"descendant")){
  pid_t p=fork();
  if(p<0)return 92;
  if(!p){if(setsid()<0)_exit(93);
   for(;;)pause();
   }
  if(write(1,"descendant-live\n",16)!=16)return 94;
  return 0;
 }
 if(!strcmp(kind,"denial")){
  const char *names[]={"/sys/fs/cgroup/cgroup.procs","/sys/fs/cgroup/pids.max","/sys/fs/cgroup/memory.max","/sys/fs/cgroup/cgroup.subtree_control"};
  for(unsigned i=0;i<4;i++){int fd=open(names[i],O_WRONLY|O_CLOEXEC);
   if(fd>=0){close(fd);
    return 95;
    }}
  /* Root capability descriptors must not survive exec (including stdin). */
  for(int fd=3;fd<65536;fd++)if(fcntl(fd,F_GETFD)>=0)return 96;
  if(write(1,"controls-denied\n",16)!=16)return 97;
  return 0;
 }
 if(!strcmp(kind,"allowance")||!strcmp(kind,"overflow")){
  unsigned char block[1024];
  memset(block,'x',sizeof block);
  unsigned count=!strcmp(kind,"overflow")?8193:8192;
  while(count){size_t n=count>sizeof block?sizeof block:count;
   ssize_t wrote=write(1,block,n);
   if(wrote<0){if(errno==EINTR)continue;
    return 98;
    }count-=(unsigned)wrote;
   }return 0;
 }
 return 99;
}
static int fx_copy_file(const char *from,const char *to,uint64_t *bytes,mode_t mode) {
 int in=open(from,O_RDONLY|O_CLOEXEC|O_NOFOLLOW),out=-1;
 struct stat s;
 unsigned char block[4096];
 int ok=0;
 if(in<0||fstat(in,&s)||!S_ISREG(s.st_mode)||s.st_size<0||(uint64_t)s.st_size>UINT64_C(134217728)-*bytes)goto done;
 out=open(to,O_WRONLY|O_CREAT|O_EXCL|O_CLOEXEC|O_NOFOLLOW,mode);
 if(out<0)goto done;
 for(;;){ssize_t n=read(in,block,sizeof block);
  if(n<0){if(errno==EINTR)continue;
   goto done;
   }if(!n)break;
  if((uint64_t)n>UINT64_C(134217728)-*bytes)goto done;
  ssize_t at=0;
  while(at<n){ssize_t w=write(out,block+at,(size_t)(n-at));
   if(w<=0)goto done;
   at+=w;
   }*bytes+=(uint64_t)n;
  }
 ok=fchmod(out,mode)==0;
 done:if(in>=0)close(in);
 if(out>=0)close(out);
 return ok;
}
static int fx_map(long pid) {
 char path[64];
 snprintf(path,sizeof path,"/proc/%ld",pid);
 int proc=open(path,O_RDONLY|O_DIRECTORY|O_CLOEXEC|O_NOFOLLOW);
 if(proc<0)return 0;
 int ok=fx_write(proc,"setgroups","deny\n")&&fx_write(proc,"uid_map","0 60000 1\n32768 60001 1\n")&&fx_write(proc,"gid_map","0 60000 1\n32768 60001 1\n");
 close(proc);
 return ok;
}
static int fx_foreign_denied(int control,int command) {
 int fd=-1;
 long pid=fx_clone(control,&fd);
 if(pid<0)return 0;
 if(!pid){if(setgroups(0,NULL)||setresgid(60001,60001,60001)||setresuid(60001,60001,60001))_exit(2);
  int procs=openat(command,"cgroup.procs",O_WRONLY|O_CLOEXEC|O_NOFOLLOW);
  if(procs>=0){close(procs);
   _exit(3);
   }_exit(errno==EACCES||errno==EPERM?0:4);
  }
 int result=0;
 while(fx_alive()){siginfo_t info;
  memset(&info,0,sizeof info);
  if(waitid(P_PIDFD,(id_t)fd,&info,WEXITED|WNOHANG))break;
  if(info.si_pid){result=info.si_code==CLD_EXITED&&info.si_status==0;
   close(fd);
   return result;
   }struct pollfd p={fd,POLLIN,0};
  poll(&p,1,10);
  }
 fx_pidfd_kill(fd);
 fx_wait_exit(fd);
 close(fd);
 return 0;
}
static int fx_client(int requester,const char *socket_path,const char *dotnet,const char *dll,const char *cwd,const char *kind,
 const char *binding,const char *input,int *pidfd) {
 long pid=fx_clone(requester,pidfd);
 if(pid<0)return 0;
 if(!pid){
  if(setgroups(0,NULL)||setresgid(60001,60001,60001)||setresuid(60001,60001,60001))_exit(110);
  int channel=socket(AF_UNIX,SOCK_STREAM|SOCK_CLOEXEC,0);
  struct sockaddr_un address;
  memset(&address,0,sizeof address);
  address.sun_family=AF_UNIX;
  if(channel<0||strlen(socket_path)>=sizeof address.sun_path)_exit(111);
  strcpy(address.sun_path,socket_path);
  if(connect(channel,(struct sockaddr *)&address,sizeof address)||dup2(channel,3)<0||fcntl(3,F_SETFD,0)||syscall(SYS_close_range,4u,~0u,0u))_exit(112);
  /* The closed binding value has a 2048-byte buffer; reserve its key too. */
  char binding_env[sizeof "FSGG_LEARN_CUSTODY_BINDING="+2048],input_env[128],cwd_env[4096],kind_env[128];
  int binding_size=snprintf(binding_env,sizeof binding_env,"FSGG_LEARN_CUSTODY_BINDING=%s",binding);
  int input_size=snprintf(input_env,sizeof input_env,"FSGG_LEARN_NATIVE_INPUT=%s",input);
  int cwd_size=snprintf(cwd_env,sizeof cwd_env,"FSGG_LEARN_NATIVE_CWD=%s",cwd);
  int kind_size=snprintf(kind_env,sizeof kind_env,"FSGG_LEARN_NATIVE_CASE=%s",kind);
  if(binding_size<0||(size_t)binding_size>=sizeof binding_env||input_size<0||(size_t)input_size>=sizeof input_env||
   cwd_size<0||(size_t)cwd_size>=sizeof cwd_env||kind_size<0||(size_t)kind_size>=sizeof kind_env)_exit(114);
  char *env[]={"PATH=/usr/bin:/bin","LC_ALL=C","DOTNET_PROCESSOR_COUNT=1","DOTNET_EnableDiagnostics=0","DOTNET_gcServer=0","DOTNET_GCHeapHardLimit=10000000",
   "PERSISTENT_V3_CUSTODY_NATIVE_FIXTURE=1","FSGG_LEARN_CUSTODY_FD=3",binding_env,input_env,cwd_env,kind_env,NULL};
  char *args[]={(char *)dotnet,(char *)dll,NULL};
  execve(dotnet,args,env);
  _exit(113);
 }
 return 1;
}
static int fx_run_case(struct shape *f,int operation,int control,int requester,int command,int user,int mount_ns,int keeper,
 int executable,int supervisor,int cwd,const char *socket_path,const char *dotnet,const char *dll,const char *directory,const char *kind,
 uint64_t *captured,int *uncertain) {
 int listen_fd=-1,channel=-1,client=-1,result=0;
 lc_adapter *a=NULL;
 const char *failure=NULL;
 char identity[65],lease[65],binding_json[1024],binding[2048],utc_text[64];
 unsigned char *frame=NULL,*text=NULL;
 cc_node *nodes=NULL;
 cc_lease *leases=NULL;
 if(!fx_random(identity)||!fx_random(lease))goto done;
 f->o.operation_id=view(identity);
 f->o.custody_identity=view(identity);
 f->slots[0].command_identity=view(lease);
 f->args[1]=view(!strcmp(kind,"partial-ack")?"allowance":(!strcmp(kind,"disconnect")?"descendant":kind));
 f->slots[0].working_directory=view(directory);
 f->o.aggregate_output_bytes=(!strcmp(kind,"allowance")||!strcmp(kind,"overflow"))?8192:CC_FRAME_MAX;
 struct tm utc;
 time_t seconds=(time_t)(f->o.deadline_utc_ms/1000);
 if(!gmtime_r(&seconds,&utc))goto done;
 char date[32];
 strftime(date,sizeof date,"%Y-%m-%dT%H:%M:%S",&utc);
 snprintf(utc_text,sizeof utc_text,"%s.%03lluZ",date,(unsigned long long)(f->o.deadline_utc_ms%1000));
 snprintf(binding_json,sizeof binding_json,"{\"schema\":\"%s\",\"identity\":\"%s\",\"deadline\":\"%s\",\"admissionSha256\":\"%.*s\"}",CC_SCHEMA,identity,utc_text,64,f->o.admission_sha256.data);
 if(!fx_base64(binding_json,binding,sizeof binding))goto done;
 listen_fd=socket(AF_UNIX,SOCK_STREAM|SOCK_CLOEXEC|SOCK_NONBLOCK,0);
 if(listen_fd<0)goto done;
 struct sockaddr_un address;
 memset(&address,0,sizeof address);
 address.sun_family=AF_UNIX;
 if(strlen(socket_path)>=sizeof address.sun_path)goto done;
 strcpy(address.sun_path,socket_path);
 if(bind(listen_fd,(struct sockaddr *)&address,sizeof address)||chmod(socket_path,0666)||listen(listen_fd,1))goto done;
 if(!fx_client(requester,socket_path,dotnet,dll,directory,kind,binding,(const char *)f->o.expected_input_sha256.data,&client))goto done;
 while(fx_alive()){channel=accept4(listen_fd,NULL,NULL,SOCK_CLOEXEC|SOCK_NONBLOCK);
  if(channel>=0)break;
  if(errno!=EAGAIN&&errno!=EINTR)goto done;
  struct pollfd p={listen_fd,POLLIN,0};
  poll(&p,1,10);
  }
 if(channel<0)goto done;
 if(!strcmp(kind,"partial-ack")){int size=128;
  if(setsockopt(channel,SOL_SOCKET,SO_SNDBUF,&size,sizeof size))goto done;
  }
 int fds[]={operation,control,requester,command,user,mount_ns,executable,supervisor,cwd,channel,keeper};
 for(unsigned i=0;i<11;i++)if(!fx_cap(&f->caps[i],(lc_role)(i+1),(i==3||i==6||i==7||i==8)?0:UINT32_MAX,fds[i]))goto done;
 frame=malloc(CC_FRAME_MAX);
 text=malloc(CC_FRAME_MAX);
 nodes=calloc(4096,sizeof *nodes);
 leases=calloc(1,sizeof *leases);
 if(!frame||!text||!nodes||!leases)goto done;
 cc_workspace workspace={.frame=frame,.text=text,.frame_capacity=CC_FRAME_MAX,.text_capacity=CC_FRAME_MAX,.nodes=nodes,.node_capacity=4096};
 a=lc_open(&f->o,workspace,leases,1,&failure);
 if(!a){fprintf(stderr,"BLOCKED native case=%s actual-open=%s\n",kind,failure);
  goto done;
  }
 int served=lc_serve(a);
 lc_observation observed;
 memset(&observed,0,sizeof observed);
 int has=lc_observe_original(a,0,&observed);
 if(has){uint64_t count=observed.stdout_captured+observed.stderr_captured;
  if(count>CC_FRAME_MAX-*captured){fprintf(stderr,"FAIL original native aggregate capture bound\n");
   goto done;
   }*captured+=count;
  printf("native case=%s identity=%s lease=%s pidfd=%d leader=%d empty=%d stdoutEOF=%d stderrEOF=%d captured=%llu/%llu cleaned=%d first=%s\n",
   kind,identity,lease,observed.retained_pidfd,observed.leader_exit_known,observed.recursive_empty,observed.stdout_eof,observed.stderr_eof,
   (unsigned long long)observed.stdout_captured,(unsigned long long)observed.stderr_captured,observed.cleanup_settled,lc_failure(a)?lc_failure(a):"none");
 }
 if(!strcmp(kind,"descendant"))result=a->owned[0].leader_left_descendants;
 int saw_descendant=a->owned[0].leader_left_descendants;
 printf("native case=%s actual-leader-exited-with-population=%d\n",kind,saw_descendant);
 int negative=!strcmp(kind,"overflow")||!strcmp(kind,"wrong-command")||!strcmp(kind,"disconnect")||!strcmp(kind,"partial-ack");
 const char *cause=lc_failure(a);
 result=negative?!served&&cause!=NULL:served;
 /* A different adapter failure is never evidence for the expected refusal. */
 if(!strcmp(kind,"overflow"))result=result&&cause&&!strcmp(cause,"capture-allowance-exceeded")&&
  has&&a->next_slot==1&&a->owned[0].born&&observed.retained_pidfd>=0&&
  observed.stdout_captured==8193&&observed.stderr_captured==0&&f->o.aggregate_output_bytes==8192;
 if(!strcmp(kind,"wrong-command"))result=result&&cause&&!strcmp(cause,"exact-root-command-mismatch")&&
  has&&a->next_slot==1&&!a->owned[0].born&&observed.retained_pidfd<0&&
  observed.stdout_captured==0&&observed.stderr_captured==0;
 if(!strcmp(kind,"disconnect")||!strcmp(kind,"partial-ack")){
  int loss=cause&&(!strcmp(cause,"request-disconnect-or-loss")||!strcmp(cause,"request-delivery-loss")||
   !strcmp(cause,"acknowledgment-delivery-unknown")||!strcmp(cause,"channel-lost"));
  result=result&&loss&&has&&a->next_slot==1&&a->owned[0].born&&observed.retained_pidfd>=0;
  if(!strcmp(kind,"partial-ack"))result=result&&observed.stdout_captured==8192&&observed.stderr_captured==0&&a->partial_acknowledgment;
 }
 if(!strcmp(kind,"descendant"))result=result&&saw_descendant&&a->owned[0].orphan_reaped>0;
 printf("native case=%s actual-owned-orphans=%u reaped=%u requester-zombie=%d\n",kind,a->owned[0].orphan_count,a->owned[0].orphan_reaped,!pidfd_alive(a->requester_pidfd));
 if(!strcmp(kind,"wrong-command"))result=result&&a->next_slot==1&&!a->owned[0].born;
 if(!strcmp(kind,"partial-ack"))result=result&&a->partial_acknowledgment;
 printf("native case=%s actual-partial-acknowledgment=%d\n",kind,a->partial_acknowledgment);
 if(has&&observed.retained_pidfd>=0)result=result&&observed.leader_exit_known&&observed.recursive_empty&&observed.stdout_eof&&observed.stderr_eof&&observed.cleanup_settled;
 /* Keep the authenticated requester unreaped while exercising the actual
  * command reaper. Its zombie is a trusted sibling, never command custody. */
 if(channel>=0){shutdown(channel,SHUT_RDWR);
  close(channel);
  channel=-1;
  }
 if(!strcmp(kind,"disconnect")){
  uint64_t now_utc,now_boot,limit;
  int zombie=0;
  const char *first=lc_failure(a);
  if(!clocks(&now_utc,&now_boot))limit=0;
  else limit=now_boot+UINT64_C(2000000000);
  while(fx_alive()&&clocks(&now_utc,&now_boot)&&now_boot<limit){siginfo_t info;
   memset(&info,0,sizeof info);
   if(waitid(P_PIDFD,(id_t)client,&info,WEXITED|WNOHANG|WNOWAIT))break;
   if(info.si_pid){zombie=info.si_code==CLD_EXITED&&info.si_status==0;
    break;
    }
   struct pollfd p={client,POLLIN,0};
   poll(&p,1,10);
  }
  int joined=pump(a,0)&&lc_failure(a)==first;
  siginfo_t still;
  memset(&still,0,sizeof still);
  int retained=zombie&&!waitid(P_PIDFD,(id_t)client,&still,WEXITED|WNOHANG|WNOWAIT)&&still.si_pid;
  result=result&&zombie&&joined&&retained&&a->owned[0].settled;
  printf("native requester-zombie regression observed=%d command-reaper-joined=%d requester-retained=%d\n",zombie,joined,retained);
 }
 if(!lc_release(a)){*uncertain=1;
  a=NULL;
  result=0;
  goto done;
  }a=NULL;
 done:if(a){if(!lc_release(a))*uncertain=1;
  }
 if(client>=0){int exited=0;
  uint64_t wait_utc,wait_boot,client_limit;
  if(!clocks(&wait_utc,&wait_boot))client_limit=0;
  else client_limit=wait_boot+UINT64_C(2000000000);
  while(fx_alive()&&clocks(&wait_utc,&wait_boot)&&wait_boot<client_limit){siginfo_t info;
   memset(&info,0,sizeof info);
   if(waitid(P_PIDFD,(id_t)client,&info,WEXITED|WNOHANG))break;
   if(info.si_pid){exited=1;
    if(info.si_code!=CLD_EXITED||info.si_status!=0)result=0;
    break;
    }struct pollfd p={client,POLLIN,0};
   poll(&p,1,10);
   }
  if(!exited){result=0;
   fx_pidfd_kill(client);
   if(!fx_wait_exit(client))*uncertain=1;
   }close(client);
  }
 if(channel>=0)close(channel);
 if(listen_fd>=0)close(listen_fd);
 if(socket_path[0])unlink(socket_path);
 if(!*uncertain){free(frame);
  free(text);
  free(nodes);
  free(leases);
  } /* Never free an unsettled adapter's workspace. */
 return result;
}
static int fx_native(const char *binary,const char *dotnet,const char *source_dll) {
 const char *parent_raw=getenv("FSGG_LEARN_NATIVE_PARENT_FD"),*home_raw=getenv("FSGG_LEARN_NATIVE_HOME_FD");
 const char *pins[]={getenv("FSGG_LEARN_NATIVE_ADMISSION_SHA256"),getenv("FSGG_LEARN_NATIVE_SOURCE_SHA256"),
  getenv("FSGG_LEARN_NATIVE_ADAPTER_SHA256"),getenv("FSGG_LEARN_CUSTODY_CORE_TOOLCHAIN_SHA256"),getenv("FSGG_LEARN_NATIVE_PROFILE_SHA256")};
 int parent=-1,home=-1,operation=-1,control=-1,requester=-1,command=-1,keeper=-1,user=-1,mount_ns=-1,executable=-1,supervisor=-1,cwd=-1;
 int ready[2]={-1,-1},mapped[2]={-1,-1},prepared[2]={-1,-1},moved=0,uncertain=0,accepted=0,setup=0;
 char root[]="/tmp/fsgg-learn-native-XXXXXX",name[96],copied_binary[256]={0},copied_dll[256]={0},socket_path[256]={0},proc_path[128];
 uint64_t disk=0,captured=0,utc,boot;
 long keeper_pid=-1;
 struct shape f;
 shape(&f);
 /* These pins are joins to the outer accepted operation; they are expressly
  * insufficient to admit this effect. The protected caller must authenticate
  * its actual dispatch/source/profile and inherited delegations before entry. */
 if(geteuid()!=0||!parent_raw||!home_raw){fprintf(stderr,"BLOCKED native root/delegated-parent-and-home-capabilities-required\n");
  return 42;
  }
 for(unsigned i=0;i<5;i++)if(!pins[i]||!digest(view(pins[i]))){fprintf(stderr,"BLOCKED native original admission/provenance joins missing\n");
  return 42;
  }
 char *end;
 errno=0;
 long parsed=strtol(parent_raw,&end,10);
 if(errno||*end||parsed<3||parsed>=65536)return 42;
 parent=fcntl((int)parsed,F_DUPFD_CLOEXEC,3);
 errno=0;
 parsed=strtol(home_raw,&end,10);
 if(errno||*end||parsed<3||parsed>=65536)goto done;
 home=fcntl((int)parsed,F_DUPFD_CLOEXEC,3);
 lc_capability actual_parent,actual_home;
 if(parent<0||home<0||!fx_cap(&actual_parent,LC_OPERATION_CGROUP,UINT32_MAX,parent)||!fx_cap(&actual_home,LC_CONTROL_LEAF,UINT32_MAX,home)||
  !verify_cap(&actual_parent)||!verify_cap(&actual_home)||!ancestor(home,parent)||!membership((long)getpid(),home)||!protected_controls(parent)||!file_equal(parent,"cgroup.type","domain\n")||
  !number_equal(parent,"pids.max",32)||!number_equal(parent,"memory.max",536870912)||!file_equal(parent,"cpu.max","100000 100000\n")){
  fprintf(stderr,"BLOCKED native actual delegated original aggregate profile or controller membership\n");
  goto done;
  }
 if(!clocks(&utc,&boot)||boot>UINT64_MAX-UINT64_C(120000000000)||utc>UINT64_MAX-120000)goto done;
 fx_deadline=boot+UINT64_C(120000000000);
 f.o.admitted_utc_ms=utc;
 f.o.deadline_utc_ms=utc+120000;
 f.o.admitted_boot_ns=boot;
 f.o.deadline_boot_ns=fx_deadline;
 f.o.admission_sha256=view(pins[0]);
 f.o.source_sha256=view(pins[1]);
 f.o.adapter_binary_sha256=view(pins[2]);
 f.o.toolchain_closure_sha256=view(pins[3]);
 f.o.native_profile_sha256=view(pins[4]);
 f.o.expected_input_sha256=view(pins[0]);
 if(!mkdtemp(root))goto done;
 setup=1;
 if(chmod(root,0755))goto done;
 snprintf(copied_binary,sizeof copied_binary,"%s/workload",root);
 if(!fx_copy_file(binary,copied_binary,&disk,0555))goto done;
 const char *files[]={"PersistentV3.ImageClosure.Tests.dll","PersistentV3.ImageClosure.Tests.deps.json","PersistentV3.ImageClosure.Tests.runtimeconfig.json","PersistentV3.ImageClosure.dll","PersistentV3.Preparation.dll","FSharp.Core.dll"};
 const char *slash=strrchr(source_dll,'/');
 if(!slash||strcmp(slash+1,files[0])||dotnet[0]!='/')goto done;
 for(unsigned i=0;i<6;i++){char from[4096],to[256];
  if((size_t)(slash-source_dll)+strlen(files[i])+2>sizeof from)goto done;
  snprintf(from,sizeof from,"%.*s/%s",(int)(slash-source_dll),source_dll,files[i]);
  snprintf(to,sizeof to,"%s/%s",root,files[i]);
  if(!fx_copy_file(from,to,&disk,0444))goto done;
  }
 snprintf(copied_dll,sizeof copied_dll,"%s/%s",root,files[0]);
 snprintf(socket_path,sizeof socket_path,"%s/channel",root);
 char nonce[65];
 if(!fx_random(nonce))goto done;
 snprintf(name,sizeof name,"fsgg-learn-native-%.32s",nonce);
 operation=fx_child_group(parent,name);
 if(operation<0){fprintf(stderr,"BLOCKED native actual fresh delegation mkdir errno=%d\n",errno);
  goto done;
  }
 if(!fx_write(operation,"pids.max","32\n")||!fx_write(operation,"memory.max","536870912\n")||!fx_write(operation,"cpu.max","100000 100000\n")||
  !fx_write(operation,"cgroup.subtree_control","+pids +memory +cpu\n"))goto done;
 control=fx_child_group(operation,"control");
 requester=fx_child_group(operation,"requester");
 command=fx_child_group(operation,"command");
 if(control<0||requester<0||command<0)goto done;
 char self[32];
 snprintf(self,sizeof self,"%ld\n",(long)getpid());
 if(!fx_write(control,"cgroup.procs",self))goto done;
 moved=1;
 if(pipe2(ready,O_CLOEXEC)||pipe2(mapped,O_CLOEXEC)||pipe2(prepared,O_CLOEXEC))goto done;
 keeper_pid=fx_clone(control,&keeper);
 if(keeper_pid<0){fprintf(stderr,"BLOCKED native atomic setup clone3 errno=%d\n",errno);
  goto done;
  }
 if(!keeper_pid){close(ready[0]);
  close(mapped[1]);
  close(prepared[0]);
  fx_keeper(copied_binary,ready[1],mapped[0],prepared[1]);
  _exit(105);
  }
 close(ready[1]);
 ready[1]=-1;
 close(mapped[0]);
 mapped[0]=-1;
 close(prepared[1]);
 prepared[1]=-1;
 if(!fx_wait_byte(ready[0])||!fx_map(keeper_pid)||write(mapped[1],"1",1)!=1||!fx_wait_byte(prepared[0])){
  fprintf(stderr,"BLOCKED native actual namespace/map/mount setup; no workload birth\n");
  goto done;
  }
 snprintf(proc_path,sizeof proc_path,"/proc/%ld/ns/user",keeper_pid);
 user=open(proc_path,O_RDONLY|O_CLOEXEC);
 snprintf(proc_path,sizeof proc_path,"/proc/%ld/ns/mnt",keeper_pid);
 mount_ns=open(proc_path,O_RDONLY|O_CLOEXEC);
 snprintf(proc_path,sizeof proc_path,"/proc/%ld/root/usr/sbin/podman",keeper_pid);
 executable=open(proc_path,O_RDONLY|O_CLOEXEC|O_NOFOLLOW);
 supervisor=open("/usr/bin/setsid",O_RDONLY|O_CLOEXEC|O_NOFOLLOW);
 cwd=open(root,O_RDONLY|O_DIRECTORY|O_CLOEXEC|O_NOFOLLOW);
 if(user<0||mount_ns<0||executable<0||supervisor<0||cwd<0||!pidfd_alive(keeper)||!fx_foreign_denied(control,command))goto done;
 char map_path[64],actual_uid[256],actual_gid[256];
 snprintf(map_path,sizeof map_path,"/proc/%ld",keeper_pid);
 int proc=open(map_path,O_RDONLY|O_DIRECTORY|O_CLOEXEC|O_NOFOLLOW);
 if(proc<0||!read_at(proc,"uid_map",actual_uid,sizeof actual_uid)||!read_at(proc,"gid_map",actual_gid,sizeof actual_gid)){if(proc>=0)close(proc);
  goto done;
  }close(proc);
 printf("native actual uid_map=%s gid_map=%s foreign-insertion=DENIED original-parent=%d operation=%s disk=%llu\n",actual_uid,actual_gid,parent,name,(unsigned long long)disk);
 const char *cases[]={"normal","descendant","denial","allowance","overflow","wrong-command","disconnect","partial-ack"};
 accepted=1;
 for(unsigned i=0;i<8;i++){
  uint64_t case_utc,case_boot;
  if(!clocks(&case_utc,&case_boot)||case_boot>=fx_deadline-UINT64_C(15000000000)||!pidfd_alive(keeper)||!fx_run_case(&f,operation,control,requester,command,user,mount_ns,keeper,executable,supervisor,cwd,
   socket_path,dotnet,copied_dll,root,cases[i],&captured,&uncertain)){accepted=0;
   break;
   }
  if(uncertain)break;
 }
 done:
 /* Preserve original subtree/first cause on any unknown. Do not rerun a case.
  * Actual parent/controller-home handles remain with the outer root profile. */
 if(command>=0){int pop;
  if(!populated(command,&pop))uncertain=1;
  else if(pop){if(!fx_write(command,"cgroup.kill","1\n"))uncertain=1;
   while(fx_alive()&&pop){if(!populated(command,&pop)){uncertain=1;
     break;
     }struct pollfd p={-1,0,0};
    poll(&p,0,10);
    }if(pop)uncertain=1;
   }}
 if(keeper>=0){fx_pidfd_kill(keeper);
  if(!fx_wait_exit(keeper))uncertain=1;
  }
 if(moved){char self[32];
  snprintf(self,sizeof self,"%ld\n",(long)getpid());
  if(!fx_write(home,"cgroup.procs",self)||!membership((long)getpid(),home))uncertain=1;
  }
 int fds[]={user,mount_ns,executable,supervisor,cwd,keeper,ready[0],ready[1],mapped[0],mapped[1],prepared[0],prepared[1],command,requester,control};
 for(unsigned i=0;i<sizeof fds/sizeof fds[0];i++)if(fds[i]>=0)close(fds[i]);
 if(operation>=0&&!uncertain){int pop;
  if(!populated(operation,&pop)||pop)uncertain=1;
  else if(unlinkat(operation,"command",AT_REMOVEDIR)||unlinkat(operation,"requester",AT_REMOVEDIR)||unlinkat(operation,"control",AT_REMOVEDIR)||unlinkat(parent,name,AT_REMOVEDIR))uncertain=1;
  }
 if(operation>=0)close(operation);
 if(home>=0)close(home);
 if(parent>=0)close(parent);
 if(setup&&!uncertain){unlink(copied_binary);
  const char *files[]={"PersistentV3.ImageClosure.Tests.dll","PersistentV3.ImageClosure.Tests.deps.json","PersistentV3.ImageClosure.Tests.runtimeconfig.json","PersistentV3.ImageClosure.dll","PersistentV3.Preparation.dll","FSharp.Core.dll"};
  for(unsigned i=0;i<6;i++){char path[256];
   snprintf(path,sizeof path,"%s/%s",root,files[i]);
   unlink(path);
   }if(socket_path[0])unlink(socket_path);
  if(rmdir(root))uncertain=1;
  }
 printf("native fixture result=%s captured=%llu cleanup=%s retained-root=%s\n",accepted&&!uncertain?"PASS":"BLOCKED_OR_FAILED",(unsigned long long)captured,uncertain?"UNKNOWN":"CLEANED",uncertain?root:"none");
 return accepted&&!uncertain?0:(uncertain?43:42);
}
int main(int argc,char **argv) {
 if(argc==3&&!strcmp(argv[1],"--custody-fixture-workload"))return fx_workload(argv[2]);
 if(argc==4&&!strcmp(argv[1],"--native-fixture"))return fx_native(argv[0],argv[2],argv[3]);
 if(argc!=1){fprintf(stderr,"focused custody source/native fixture arguments refused\n");
  return 2;
  }
 return fsgg_custody_adapter_controls();
}
#endif
