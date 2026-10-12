#define _GNU_SOURCE
#include "../../deployment/telemetry-collector/persistent/v3/image-closure/native/custody_adapter.c"
/* Include the production implementation in this focused translation unit to
 * exercise its actual private match/capture/accounting guards. No replacement
 * syscalls, return-value mocks or arbitrary retirement callback is supplied. */
#include <errno.h>
#include <limits.h>
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
static int fx_decimal(const char *text,uint64_t *value) {
 if(!text||!*text)return 0;
 uint64_t n=0;
 for(const unsigned char *p=(const unsigned char *)text;*p;p++){
  if(*p<'0'||*p>'9'||n>(UINT64_MAX-(uint64_t)(*p-'0'))/10)return 0;
  n=n*10+(uint64_t)(*p-'0');
 }
 *value=n;
 return 1;
}
static int fx_original_clock(uint64_t utc,uint64_t boot,uint64_t now_utc,uint64_t now_boot) {
 return utc>0&&boot>0&&utc<=UINT64_MAX-120000&&boot<=UINT64_MAX-UINT64_C(120000000000)&&
  now_utc>=utc&&now_boot>=boot&&now_utc<utc+105000&&now_boot<boot+UINT64_C(105000000000);
}
static int fx_ack_match(const char *expected,size_t expected_bytes,const char *actual,size_t actual_bytes) {
 return expected&&actual&&expected_bytes>0&&expected_bytes<=1024&&actual_bytes==expected_bytes&&!memcmp(expected,actual,actual_bytes);
}
/* Diagnostic state is per controller/keeper process. A later cleanup failure
 * never replaces its first observed failure, and an indirect/clock failure
 * never acquires a guessed syscall errno. No diagnostic grants custody. */
struct fx_diagnostic { const char *first_stage; int first_errno,first_errno_known; unsigned additional; };
static int fx_diagnostic_note(struct fx_diagnostic *d,const char *stage,int ok,int error,int known) {
 if(ok)return 1;
 if(!d->first_stage){d->first_stage=stage;d->first_errno=known?error:0;d->first_errno_known=known;}
 else d->additional++;
 return 0;
}
static int fx_diagnostic_text(char *out,size_t capacity,const struct fx_diagnostic *d,const char *stage,int error,int known) {
 int n=snprintf(out,capacity,"native diagnostic stage=%s errno-known=%d errno=%d first-stage=%s first-errno-known=%d first-errno=%d additional=%u\n",
  stage,known,known?error:0,d->first_stage?d->first_stage:"none",d->first_errno_known,d->first_errno,d->additional);
 return n>=0&&(size_t)n<capacity;
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
 uint64_t parsed_clock;
 check(fx_decimal("18446744073709551615",&parsed_clock)&&parsed_clock==UINT64_MAX,"component exact uint64 original clock parse");
 check(!fx_decimal("18446744073709551616",&parsed_clock),"component original clock overflow refused");
 check(!fx_decimal("-1",&parsed_clock)&&!fx_decimal("1x",&parsed_clock)&&!fx_decimal("",&parsed_clock),"component original clock non-decimal refused");
 check(fx_original_clock(1000,1000,105999,UINT64_C(104999999999)),"component work within original interval accepted");
 check(fx_original_clock(1000,1000,105999,UINT64_C(105000000999)),"component exact last BOOT work nanosecond accepted");
 check(!fx_original_clock(1000,1000,106001,1001)&&!fx_original_clock(1000,1000,1001,UINT64_C(105000001001)),"component cleanup interval cannot admit setup work");
 check(!fx_original_clock(1000,1000,999,1001),"component wall rollback cannot rebase original interval");
 check(!fx_original_clock(1000,1000,1001,999),"component boot rollback cannot rebase original interval");
 check(!fx_original_clock(1000,1000,106000,1001),"component UTC cleanup reserve refuses new setup");
 check(!fx_original_clock(1000,1000,1001,UINT64_C(105000001000)),"component BOOT cleanup reserve refuses new setup");
 check(!fx_original_clock(UINT64_MAX,1000,UINT64_MAX,1001),"component UTC interval addition overflow refused");
 check(!fx_original_clock(1000,UINT64_MAX,1001,UINT64_MAX),"component BOOT interval addition overflow refused");
 check(fx_ack_match("yes",3,"yes",3),"fixed registration exact ACK bytes accepted");
 check(!fx_ack_match("yes",3,"yes-extra",9),"fixed registration unexpected ACK field refuses");
 check(!fx_ack_match("yes",3,"ye",2),"fixed registration truncated ACK refuses");
 check(!fx_ack_match("yes",3,"no!",3)&&!fx_ack_match("yes",3,NULL,0),"fixed registration malformed or missing ACK refuses");
 struct fx_diagnostic diagnostic={0};char diagnostic_text[512];
 check(fx_diagnostic_note(&diagnostic,"setup/keeper-userns",1,EPERM,1)&&!diagnostic.first_stage,"successful setup cannot create failure");
 check(!fx_diagnostic_note(&diagnostic,"setup/keeper-userns",0,EPERM,1)&&diagnostic.first_errno==EPERM&&diagnostic.first_errno_known==1,"injected syscall failure retains exact stage and errno");
 check(!fx_diagnostic_note(&diagnostic,"cleanup/keeper-wait",0,ECHILD,1)&&!strcmp(diagnostic.first_stage,"setup/keeper-userns")&&diagnostic.first_errno==EPERM&&diagnostic.additional==1,"cleanup failure preserves original setup cause");
 check(fx_diagnostic_note(&diagnostic,"cleanup/home-restored",1,0,1)&&diagnostic.additional==1,"successful cleanup cannot erase or add failure");
 check(fx_diagnostic_text(diagnostic_text,sizeof diagnostic_text,&diagnostic,"cleanup/keeper-wait",ECHILD,1)&&strstr(diagnostic_text,"stage=cleanup/keeper-wait")&&strstr(diagnostic_text,"first-stage=setup/keeper-userns")&&strstr(diagnostic_text,"additional=1"),"actual diagnostic formatter reports first and additional causes");
 diagnostic=(struct fx_diagnostic){0};
 check(!fx_diagnostic_note(&diagnostic,"setup/original-clock",0,EPERM,0)&&diagnostic.first_errno==0&&diagnostic.first_errno_known==0,"clock failure cannot inherit stale errno");
 check(!fx_diagnostic_note(&diagnostic,"setup/keeper-ready",0,EACCES,0)&&diagnostic.first_errno==0&&diagnostic.additional==1,"indirect setup failure cannot claim syscall errno");
 check(fx_diagnostic_text(diagnostic_text,sizeof diagnostic_text,&diagnostic,"setup/keeper-ready",EACCES,0)&&strstr(diagnostic_text,"errno-known=0 errno=0"),"unknown errno renders explicitly unknown");
 check(!fx_diagnostic_text(diagnostic_text,1,&diagnostic,"setup/keeper-ready",0,0),"diagnostic overflow cannot masquerade as complete report");
 diagnostic=(struct fx_diagnostic){0};unsigned evaluated=0;
 int injected=fx_diagnostic_note(&diagnostic,"setup/first-success",++evaluated==1,0,0)&&
  fx_diagnostic_note(&diagnostic,"setup/second-failure",++evaluated==99,EIO,1)&&
  fx_diagnostic_note(&diagnostic,"setup/unreached",++evaluated==3,0,0);
 check(!injected&&evaluated==2&&!strcmp(diagnostic.first_stage,"setup/second-failure"),"injected compound setup preserves evaluation order and stops on first failure");
 check(diagnostic.first_errno==EIO&&diagnostic.additional==0,"unreached setup cannot add or replace errno");
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
/* One original interval is supplied by the admitted fixed root invocation.
 * No child/setup/case creates or renews an operation clock. */
static uint64_t fx_started_boot,fx_started_utc,fx_deadline,fx_deadline_utc;
static int fx_child_open_unknown;
static struct fx_diagnostic fx_first_failure;
static int fx_step(const char *stage,int ok,int known) {
 int saved_errno=errno;
 if(fx_diagnostic_note(&fx_first_failure,stage,ok,saved_errno,known))return 1;
 char text[512];
 if(fx_diagnostic_text(text,sizeof text,&fx_first_failure,stage,saved_errno,known))fputs(text,stderr);
 else fputs("native diagnostic reporting=BOUND first cause retained in controller state\n",stderr);
 errno=saved_errno;
 return 0;
}
static int fx_result(const char *stage,long actual,long expected) {
 return fx_step(stage,actual==expected,actual<0);
}

static int fx_alive(void){uint64_t utc,boot;
 return clocks(&utc,&boot)&&boot>=fx_started_boot&&utc>=fx_started_utc&&boot<fx_deadline&&utc<fx_deadline_utc;
 }
static int fx_work(void){uint64_t utc,boot;
 return clocks(&utc,&boot)&&fx_original_clock(fx_started_utc,fx_started_boot,utc,boot);
 }
/* Fixed root TEST registration channel. fd0 came from the existing owner's
 * one socketpair through its same Popen/sudo/sealed-controller chain. */
static struct {
 char nonce[65],root_name[96];
 long observer_pid;
 uint64_t observer_start;
 uid_t observer_uid;gid_t observer_gid;
 int observer_pidfd,registered,case_active,operation_drained,aggregate_drained;
 unsigned ordinal;
} fx_join={.observer_pidfd=-1};
static int fx_birth(long pid,uint64_t *start) {
 char path[64],text[4096];struct stat before,after;
 int n=snprintf(path,sizeof path,"/proc/%ld",pid);
 if(n<0||(size_t)n>=sizeof path)return 0;
 int fd=open(path,O_RDONLY|O_DIRECTORY|O_CLOEXEC|O_NOFOLLOW);
 if(fd<0)return 0;
 int ok=!fstat(fd,&before)&&read_at(fd,"stat",text,sizeof text)&&!fstat(fd,&after)&&
  before.st_dev==after.st_dev&&before.st_ino==after.st_ino;
 close(fd);
 if(!ok)return 0;
 char *last=strrchr(text,')'),*save=NULL;
 if(!last||last[1]!=' ')return 0;
 char *part=strtok_r(last+2," ", &save);
 for(unsigned i=0;i<19&&part;i++)part=strtok_r(NULL," ",&save);
 return part&&fx_decimal(part,start)&&*start>0;
}
static int fx_join_observer(void) {
 uint64_t start;
 return fx_join.observer_pidfd>=0&&pidfd_alive(fx_join.observer_pidfd)&&
  fx_birth(fx_join.observer_pid,&start)&&start==fx_join.observer_start&&pidfd_alive(fx_join.observer_pidfd);
}
static int fx_join_init(void) {
 const char *nonce=getenv("FSGG_LEARN_INNER_NONCE");
 uint64_t pid,uid,gid,start;
 int type=0,domain=0,pass=1;socklen_t size=sizeof(int);
 fx_join.observer_pidfd=-1;
 if(!nonce||!digest(view(nonce))||!fx_decimal(getenv("FSGG_LEARN_INNER_OBSERVER_PID"),&pid)||pid<=1||pid>INT_MAX||
  !fx_decimal(getenv("FSGG_LEARN_INNER_OBSERVER_UID"),&uid)||uid>UINT32_MAX||
  !fx_decimal(getenv("FSGG_LEARN_INNER_OBSERVER_GID"),&gid)||gid>UINT32_MAX||
  !fx_decimal(getenv("FSGG_LEARN_INNER_OBSERVER_START"),&start)||!start||!fx_work()||
  getsockopt(STDIN_FILENO,SOL_SOCKET,SO_TYPE,&type,&size)||type!=SOCK_SEQPACKET)return 0;
 size=sizeof(int);
 if(getsockopt(STDIN_FILENO,SOL_SOCKET,SO_DOMAIN,&domain,&size)||domain!=AF_UNIX||
  setsockopt(STDIN_FILENO,SOL_SOCKET,SO_PASSCRED,&pass,sizeof pass))return 0;
 memcpy(fx_join.nonce,nonce,65);fx_join.observer_pid=(long)pid;fx_join.observer_uid=(uid_t)uid;
 fx_join.observer_gid=(gid_t)gid;fx_join.observer_start=start;
 fx_join.observer_pidfd=(int)syscall(SYS_pidfd_open,(pid_t)pid,0);
 return fx_join_observer();
}
static int fx_join_ack(const char *kind,unsigned ordinal,const char *phase,const char *data,size_t bytes) {
 char expected[512];int n;
 if(!strcmp(kind,"JOIN"))n=snprintf(expected,sizeof expected,"{\"version\":1,\"nonce\":\"%s\",\"kind\":\"JOIN\",\"accepted\":true}",fx_join.nonce);
 else if(!strcmp(kind,"CASE"))n=snprintf(expected,sizeof expected,"{\"version\":1,\"nonce\":\"%s\",\"kind\":\"CASE\",\"accepted\":true,\"ordinal\":%u}",fx_join.nonce,ordinal);
 else n=snprintf(expected,sizeof expected,"{\"version\":1,\"nonce\":\"%s\",\"kind\":\"DRAIN\",\"accepted\":true,\"ordinal\":%u,\"phase\":\"%s\"}",fx_join.nonce,ordinal,phase);
 return n>0&&(size_t)n<sizeof expected&&fx_ack_match(expected,(size_t)n,data,bytes);
}
static int fx_join_packet(const char *kind,unsigned ordinal,const char *phase,const char *data,const int *fds,size_t count,int work) {
 if(count>32||strlen(data)>8192||!(work?fx_work():fx_alive())||!fx_join_observer())return 0;
 union {struct cmsghdr align;unsigned char bytes[CMSG_SPACE(sizeof(int)*32)+CMSG_SPACE(sizeof(struct ucred))];} ancillary;
 struct iovec io={.iov_base=(void *)data,.iov_len=strlen(data)};
 struct msghdr message;
 memset(&message,0,sizeof message);memset(&ancillary,0,sizeof ancillary);
 message.msg_iov=&io;message.msg_iovlen=1;
 if(count){message.msg_control=ancillary.bytes;message.msg_controllen=CMSG_SPACE(count*sizeof(int));
  struct cmsghdr *c=CMSG_FIRSTHDR(&message);c->cmsg_level=SOL_SOCKET;c->cmsg_type=SCM_RIGHTS;c->cmsg_len=CMSG_LEN(count*sizeof(int));
  memcpy(CMSG_DATA(c),fds,count*sizeof(int));
 }
 /* One atomic packet only. An ambiguous/failed send is never replayed. */
 if(sendmsg(STDIN_FILENO,&message,MSG_DONTWAIT|MSG_NOSIGNAL)!=(ssize_t)io.iov_len)return 0;
 while(work?fx_work():fx_alive()){
  struct pollfd ready={STDIN_FILENO,POLLIN,0};
  int polled=poll(&ready,1,10);
  if(polled<0){if(errno==EINTR)continue;return 0;}
  if(!polled)continue;
  char reply[1025];io.iov_base=reply;io.iov_len=sizeof reply;
  memset(&message,0,sizeof message);memset(&ancillary,0,sizeof ancillary);
  message.msg_iov=&io;message.msg_iovlen=1;message.msg_control=ancillary.bytes;message.msg_controllen=sizeof ancillary.bytes;
  ssize_t bytes=recvmsg(STDIN_FILENO,&message,MSG_DONTWAIT|MSG_CMSG_CLOEXEC);
  if(bytes<0){if(errno==EAGAIN||errno==EINTR)continue;return 0;}
  int valid=bytes>0&&bytes<=1024&&!(message.msg_flags&(MSG_TRUNC|MSG_CTRUNC)),credentials=0;
  struct ucred credential={0};
  for(struct cmsghdr *c=CMSG_FIRSTHDR(&message);c;c=CMSG_NXTHDR(&message,c)){
   if(c->cmsg_level==SOL_SOCKET&&c->cmsg_type==SCM_CREDENTIALS&&c->cmsg_len==CMSG_LEN(sizeof credential)){
    memcpy(&credential,CMSG_DATA(c),sizeof credential);credentials++;
   }else{
    valid=0;
    if(c->cmsg_level==SOL_SOCKET&&c->cmsg_type==SCM_RIGHTS&&c->cmsg_len>=CMSG_LEN(0)){
     size_t received=(c->cmsg_len-CMSG_LEN(0))/sizeof(int);int *rights=(int *)CMSG_DATA(c);
     for(size_t i=0;i<received;i++)close(rights[i]);
    }
   }
  }
  if(!valid||credentials!=1||credential.pid!=fx_join.observer_pid||credential.uid!=fx_join.observer_uid||credential.gid!=fx_join.observer_gid||
   !fx_join_observer()||!(work?fx_work():fx_alive()))return 0;
  return fx_join_ack(kind,ordinal,phase,reply,(size_t)bytes);
 }
 return 0;
}
static int fx_join_root(int parent,int home,int operation,int control,int requester,int command,const char *operation_name) {
 char data[4096];
 const char *keys[]={"FSGG_LEARN_NATIVE_ADMISSION_SHA256","FSGG_LEARN_NATIVE_SOURCE_SHA256","FSGG_LEARN_NATIVE_ADAPTER_SHA256",
  "FSGG_LEARN_CUSTODY_CORE_TOOLCHAIN_SHA256","FSGG_LEARN_NATIVE_PROFILE_SHA256"};
 const char *pins[5];for(unsigned i=0;i<5;i++){pins[i]=getenv(keys[i]);if(!pins[i]||!digest(view(pins[i])))return 0;}
 int n=snprintf(data,sizeof data,"{\"version\":1,\"kind\":\"JOIN\",\"nonce\":\"%s\",\"pins\":{\"%s\":\"%s\",\"%s\":\"%s\",\"%s\":\"%s\",\"%s\":\"%s\",\"%s\":\"%s\"},\"startUtc\":%llu,\"startBoot\":%llu,\"deadlineBoot\":%llu,\"rootName\":\"%s\",\"operationName\":\"%s\",\"roles\":[\"controllerPidfd\",\"parent\",\"home\",\"operation\",\"control\",\"requester\",\"command\"]}",
  fx_join.nonce,keys[0],pins[0],keys[1],pins[1],keys[2],pins[2],keys[3],pins[3],keys[4],pins[4],
  (unsigned long long)fx_started_utc,(unsigned long long)fx_started_boot,(unsigned long long)fx_deadline,fx_join.root_name,operation_name);
 if(n<0||(size_t)n>=sizeof data||fx_join.registered)return 0;
 int self=(int)syscall(SYS_pidfd_open,getpid(),0);
 if(self<0)return 0;
 int fds[]={self,parent,home,operation,control,requester,command};
 int ok=fx_join_packet("JOIN",0,NULL,data,fds,7,1);close(self);
 if(ok)fx_join.registered=1;
 return ok;
}
static int fx_join_case(unsigned ordinal,int requester,int channel) {
 char data[512];
 if(!fx_join.registered||fx_join.case_active||fx_join.operation_drained||ordinal!=fx_join.ordinal||ordinal>=8)return 0;
 int n=snprintf(data,sizeof data,"{\"version\":1,\"kind\":\"CASE\",\"nonce\":\"%s\",\"ordinal\":%u,\"roles\":[\"requesterPidfd\",\"abortSocket\"]}",fx_join.nonce,ordinal);
 if(n<0||(size_t)n>=sizeof data)return 0;
 int fds[]={requester,channel};
 if(!fx_join_packet("CASE",ordinal,NULL,data,fds,2,1))return 0;
 fx_join.case_active=1;return 1;
}
static int fx_join_drain(unsigned ordinal,const char *phase) {
 char data[512];
 if(!fx_join.registered||
  (!strcmp(phase,"case")&&(!fx_join.case_active||ordinal!=fx_join.ordinal))||
  (!strcmp(phase,"operation")&&(fx_join.case_active||fx_join.operation_drained||ordinal!=8))||
  (!strcmp(phase,"aggregate")&&(!fx_join.operation_drained||fx_join.aggregate_drained||ordinal!=8))||
  (strcmp(phase,"case")&&strcmp(phase,"operation")&&strcmp(phase,"aggregate")))return 0;
 int n=snprintf(data,sizeof data,"{\"version\":1,\"kind\":\"DRAIN\",\"nonce\":\"%s\",\"ordinal\":%u,\"phase\":\"%s\",\"roles\":[]}",fx_join.nonce,ordinal,phase);
 if(n<0||(size_t)n>=sizeof data||!fx_join_packet("DRAIN",ordinal,phase,data,NULL,0,0))return 0;
 if(!strcmp(phase,"case")){fx_join.case_active=0;fx_join.ordinal=ordinal+1;}
 else if(!strcmp(phase,"operation"))fx_join.operation_drained=1;
 else if(!strcmp(phase,"aggregate"))fx_join.aggregate_drained=1;
 else return 0;
 return 1;
}

static int fx_write_limit(int fd,const char *name,const char *value,int work) {
 if(!fx_step(work?"setup/control-file-clock":"cleanup/control-file-clock",work?fx_work():fx_alive(),0))return 0;
 int out=openat(fd,name,O_WRONLY|O_CLOEXEC|O_NOFOLLOW);
 size_t n=strlen(value);
 if(!fx_step(work?"setup/control-file-open":"cleanup/control-file-open",out>=0,1))return 0;
 int ok=fx_step(work?"setup/control-write-clock":"cleanup/control-write-clock",work?fx_work():fx_alive(),0)&&
  fx_result(name,write(out,value,n),(long)n);
 close(out);
 return ok;
}
static int fx_write(int fd,const char *name,const char *value) {
 return fx_write_limit(fd,name,value,0);
}
static int fx_write_work(int fd,const char *name,const char *value) {
 return fx_write_limit(fd,name,value,1);
}
static int fx_child_group(int parent,const char *name) {
 if(!fx_step("setup/owned-group-clock",fx_work(),0)||!fx_result("setup/owned-group-mkdir",mkdirat(parent,name,0755),0))return -1;
 int child=openat(parent,name,O_RDONLY|O_DIRECTORY|O_CLOEXEC|O_NOFOLLOW);
 if(!fx_step("setup/owned-group-open",child>=0,1))fx_child_open_unknown=1; /* Created identity cannot be adopted. */
 if(child>=0&&(!fx_step("setup/owned-group-mode-clock",fx_work(),0)||!fx_result("setup/owned-group-mode",fchmod(child,0755),0))){
  fx_child_open_unknown=1;close(child);return -1;
 }
 /* Only this newly created directory receives readonly observer traversal;
  * no workload/provider identity gains a writable cgroup control. */
 return child;
}
static int fx_wait_byte(int fd) {
 while(fx_work()){struct pollfd p={fd,POLLIN,0};
  if(poll(&p,1,10)>0){char c;
   return fx_result("setup/wait-byte-read",read(fd,&c,1),1)&&fx_step("setup/wait-byte-value",c=='1',0);
   }}
 return fx_step("setup/wait-byte-original-clock",0,0);
}
static void fx_pidfd_kill(int fd){if(fd>=0&&fx_alive())(void)fx_result("cleanup/owned-pidfd-signal",syscall(SYS_pidfd_send_signal,fd,SIGKILL,NULL,0),0);
 }
static int fx_wait_exit(int fd) {
 while(fx_alive()){siginfo_t info;
  memset(&info,0,sizeof info);
  if(!fx_result("cleanup/owned-pidfd-waitid",waitid(P_PIDFD,(id_t)fd,&info,WEXITED|WNOHANG),0))return 0;
  if(info.si_pid)return 1;
  struct pollfd p={fd,POLLIN,0};
  poll(&p,1,10);
  }
 return fx_step("cleanup/owned-pidfd-wait-original-clock",0,0);
}
static int fx_cap(lc_capability *c,lc_role role,uint32_t slot,int fd) {
 if(!fx_work())return 0;
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
 if(!fx_work())return 0;
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
 if(!fx_step("setup/owned-clone-clock",fx_work(),0))return -1;
 struct clone_args args;
 memset(&args,0,sizeof args);
 args.flags=CLONE_INTO_CGROUP|CLONE_PIDFD;
 args.cgroup=(uint64_t)cgroup;
 args.pidfd=(uint64_t)(uintptr_t)pidfd;
 args.exit_signal=SIGCHLD;
 long pid=syscall(SYS_clone3,&args,sizeof args);
 (void)fx_step("setup/owned-clone3",pid>=0,1);
 if(!pid)close(STDIN_FILENO);
 return pid;
}
static int fx_readonly_bind(const char *path) {
 return fx_step("setup/readonly-bind-clock",fx_work(),0)&&fx_result("setup/readonly-bind",mount(path,path,NULL,MS_BIND,NULL),0)&&
  fx_step("setup/readonly-remount-clock",fx_work(),0)&&fx_result("setup/readonly-remount",mount(NULL,path,NULL,MS_REMOUNT|MS_BIND|MS_RDONLY,NULL),0);
}
static void fx_keeper(const char *binary,int ready,int mapped,int prepared) {
 /* Parent installs exactly two singleton maps before mount setup. The new
  * mount namespace is owned by this new user namespace, matching setns order. */
 if(!fx_step("setup/keeper-before-userns-clock",fx_work(),0)||!fx_result("setup/keeper-userns",unshare(CLONE_NEWUSER),0)||
  !fx_result("setup/keeper-ready-write",write(ready,"1",1),1)||!fx_step("setup/keeper-map-ack",fx_wait_byte(mapped),0)||
  !fx_step("setup/keeper-before-gid-clock",fx_work(),0)||!fx_result("setup/keeper-setresgid",setresgid(0,0,0),0)||
  !fx_step("setup/keeper-before-uid-clock",fx_work(),0)||!fx_result("setup/keeper-setresuid",setresuid(0,0,0),0)||
  !fx_step("setup/keeper-before-mountns-clock",fx_work(),0)||!fx_result("setup/keeper-mountns",unshare(CLONE_NEWNS),0))_exit(101);
 /* Seal the parent before adding private children. A later nonrecursive
  * /usr bind would hide selected /usr/sbin and setsid submounts. */
 if(!fx_step("setup/keeper-private-mount-clock",fx_work(),0)||!fx_result("setup/keeper-private-root",mount(NULL,"/",NULL,MS_REC|MS_PRIVATE,NULL),0)||
  !fx_step("setup/keeper-readonly-usr",fx_readonly_bind("/usr"),0)||!fx_step("setup/keeper-sbin-clock",fx_work(),0)||
  !fx_result("setup/keeper-sbin-tmpfs",mount("tmpfs","/usr/sbin","tmpfs",MS_NOSUID|MS_NODEV,"size=1048576,mode=0755"),0))_exit(102);
 if(!fx_step("setup/keeper-podman-file-clock",fx_work(),0))_exit(103);
 int target=open("/usr/sbin/podman",O_CREAT|O_EXCL|O_RDONLY|O_CLOEXEC,0555);
 if(!fx_step("setup/keeper-podman-file",target>=0,1))_exit(103);
 close(target);
 if(!fx_step("setup/keeper-podman-bind-clock",fx_work(),0)||!fx_result("setup/keeper-podman-bind",mount(binary,"/usr/sbin/podman",NULL,MS_BIND,NULL),0)||
  !fx_step("setup/keeper-podman-readonly",fx_readonly_bind("/usr/sbin/podman"),0)||!fx_step("setup/keeper-setsid-readonly",fx_readonly_bind("/usr/bin/setsid"),0)||
  !fx_step("setup/keeper-cgroup-mount-clock",fx_work(),0)||!fx_result("setup/keeper-cgroup-tmpfs",mount("tmpfs","/sys/fs/cgroup","tmpfs",MS_NOSUID|MS_NODEV|MS_NOEXEC,"size=1048576,mode=0555"),0)||
  !fx_step("setup/keeper-cgroup-readonly-clock",fx_work(),0)||!fx_result("setup/keeper-cgroup-readonly",mount(NULL,"/sys/fs/cgroup",NULL,MS_REMOUNT|MS_RDONLY|MS_NOSUID|MS_NODEV|MS_NOEXEC,NULL),0)||
  !fx_step("setup/keeper-prepared-clock",fx_work(),0)||!fx_result("setup/keeper-prepared-write",write(prepared,"1",1),1))_exit(104);
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
 if(!fx_work())return 0;
 int in=open(from,O_RDONLY|O_CLOEXEC|O_NOFOLLOW),out=-1;
 struct stat s;
 unsigned char block[4096];
 int ok=0;
 if(in<0||fstat(in,&s)||!S_ISREG(s.st_mode)||s.st_size<0||(uint64_t)s.st_size>UINT64_C(134217728)-*bytes)goto done;
 if(!fx_work())goto done;
 out=open(to,O_WRONLY|O_CREAT|O_EXCL|O_CLOEXEC|O_NOFOLLOW,mode);
 if(out<0)goto done;
 for(;;){if(!fx_work())goto done;
  ssize_t n=read(in,block,sizeof block);
  if(n<0){if(errno==EINTR)continue;
   goto done;
   }if(!n)break;
  if((uint64_t)n>UINT64_C(134217728)-*bytes)goto done;
  ssize_t at=0;
  while(at<n){if(!fx_work())goto done;
   ssize_t w=write(out,block+at,(size_t)(n-at));
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
 if(!fx_step("setup/keeper-map-proc-open",proc>=0,1))return 0;
 int ok=fx_step("setup/keeper-setgroups-map",fx_write_work(proc,"setgroups","deny\n"),0)&&
  fx_step("setup/keeper-uid-map",fx_write_work(proc,"uid_map","0 60000 1\n32768 60001 1\n"),0)&&
  fx_step("setup/keeper-gid-map",fx_write_work(proc,"gid_map","0 60000 1\n32768 60001 1\n"),0);
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
 while(fx_work()){siginfo_t info;
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
  if(!fx_work()||setgroups(0,NULL)||setresgid(60001,60001,60001)||setresuid(60001,60001,60001))_exit(110);
  int channel=socket(AF_UNIX,SOCK_STREAM|SOCK_CLOEXEC,0);
  struct sockaddr_un address;
  memset(&address,0,sizeof address);
  address.sun_family=AF_UNIX;
  if(channel<0||strlen(socket_path)>=sizeof address.sun_path)_exit(111);
  strcpy(address.sun_path,socket_path);
  if(!fx_work()||connect(channel,(struct sockaddr *)&address,sizeof address)||dup2(channel,3)<0||fcntl(3,F_SETFD,0)||syscall(SYS_close_range,4u,~0u,0u))_exit(112);
  /* The closed binding value has a 2048-byte buffer; reserve its key too. */
  char binding_env[sizeof "FSGG_LEARN_CUSTODY_BINDING="+2048],input_env[128],cwd_env[4096],kind_env[128];
  int binding_size=snprintf(binding_env,sizeof binding_env,"FSGG_LEARN_CUSTODY_BINDING=%s",binding);
  int input_size=snprintf(input_env,sizeof input_env,"FSGG_LEARN_NATIVE_INPUT=%s",input);
  int cwd_size=snprintf(cwd_env,sizeof cwd_env,"FSGG_LEARN_NATIVE_CWD=%s",cwd);
  int kind_size=snprintf(kind_env,sizeof kind_env,"FSGG_LEARN_NATIVE_CASE=%s",kind);
  if(binding_size<0||(size_t)binding_size>=sizeof binding_env||input_size<0||(size_t)input_size>=sizeof input_env||
   cwd_size<0||(size_t)cwd_size>=sizeof cwd_env||kind_size<0||(size_t)kind_size>=sizeof kind_env)_exit(114);
  char *env[]={"PATH=/usr/bin:/bin","LC_ALL=C","DOTNET_PROCESSOR_COUNT=1","DOTNET_EnableDiagnostics=0","DOTNET_gcServer=0","DOTNET_GCHeapHardLimit=10000000","DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1",
   "PERSISTENT_V3_CUSTODY_NATIVE_FIXTURE=1","FSGG_LEARN_CUSTODY_FD=3",binding_env,input_env,cwd_env,kind_env,NULL};
  char *args[]={(char *)dotnet,(char *)dll,NULL};
  if(!fx_work())_exit(115);
  execve(dotnet,args,env);
  _exit(113);
 }
 return 1;
}
static int fx_run_case(struct shape *f,int operation,int control,int requester,int command,int user,int mount_ns,int keeper,
 int executable,int supervisor,int cwd,const char *socket_path,const char *dotnet,const char *dll,const char *directory,const char *kind,
 uint64_t *captured,int *uncertain,unsigned ordinal) {
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
 if(!fx_work()||bind(listen_fd,(struct sockaddr *)&address,sizeof address)||chmod(socket_path,0666)||listen(listen_fd,1))goto done;
 if(!fx_client(requester,socket_path,dotnet,dll,directory,kind,binding,(const char *)f->o.expected_input_sha256.data,&client))goto done;
 while(fx_work()){channel=accept4(listen_fd,NULL,NULL,SOCK_CLOEXEC|SOCK_NONBLOCK);
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
 if(!fx_join_case(ordinal,client,channel)){*uncertain=1;goto done;}
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
   }
  if(fx_join.case_active&&fx_join.ordinal==ordinal&&!fx_join_drain(ordinal,"case")){*uncertain=1;result=0;}
  close(client);
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
static int fx_native(const char *binary,const char *dotnet,const char *source_dll,
 int supplied_parent,int supplied_home,const char *storage,uint64_t admitted_utc,uint64_t admitted_boot) {
 const char *pins[]={getenv("FSGG_LEARN_NATIVE_ADMISSION_SHA256"),getenv("FSGG_LEARN_NATIVE_SOURCE_SHA256"),
  getenv("FSGG_LEARN_NATIVE_ADAPTER_SHA256"),getenv("FSGG_LEARN_CUSTODY_CORE_TOOLCHAIN_SHA256"),getenv("FSGG_LEARN_NATIVE_PROFILE_SHA256")};
 int parent=-1,home=-1,operation=-1,control=-1,requester=-1,command=-1,keeper=-1,user=-1,mount_ns=-1,executable=-1,supervisor=-1,cwd=-1;
 int ready[2]={-1,-1},mapped[2]={-1,-1},prepared[2]={-1,-1},moved=0,uncertain=0,accepted=0,setup=0;
 char root[256]={0},name[96],copied_binary[256]={0},copied_dll[256]={0},socket_path[256]={0},proc_path[128];
 uint64_t disk=0,captured=0,utc,boot;
 long keeper_pid=-1;
 struct shape f;
 shape(&f);
 /* These pins are joins to the outer accepted operation; they are expressly
  * insufficient to admit this effect. The protected caller must authenticate
  * its actual dispatch/source/profile and inherited delegations before entry. */
 if(geteuid()!=0||supplied_parent<3||supplied_home<3){fprintf(stderr,"BLOCKED native root/delegated-parent-and-home-capabilities-required\n");
  return 42;
  }
 for(unsigned i=0;i<5;i++)if(!pins[i]||!digest(view(pins[i]))){fprintf(stderr,"BLOCKED native original admission/provenance joins missing\n");
  return 42;
  }
 parent=fcntl(supplied_parent,F_DUPFD_CLOEXEC,3);
 home=fcntl(supplied_home,F_DUPFD_CLOEXEC,3);
 lc_capability actual_parent,actual_home;
 if(parent<0||home<0||!fx_cap(&actual_parent,LC_OPERATION_CGROUP,UINT32_MAX,parent)||!fx_cap(&actual_home,LC_CONTROL_LEAF,UINT32_MAX,home)||
  !verify_cap(&actual_parent)||!verify_cap(&actual_home)||!ancestor(home,parent)||!membership((long)getpid(),home)||!protected_controls(parent)||!file_equal(parent,"cgroup.type","domain\n")||
  !number_equal(parent,"pids.max",32)||!number_equal(parent,"memory.max",536870912)||!file_equal(parent,"cpu.max","100000 100000\n")){
  fprintf(stderr,"BLOCKED native actual delegated original aggregate profile or controller membership\n");
  goto done;
  }
 if(!clocks(&utc,&boot)||!fx_work()||boot<admitted_boot||utc<admitted_utc||
  admitted_boot>UINT64_MAX-UINT64_C(120000000000)||admitted_utc>UINT64_MAX-120000||
  fx_deadline!=admitted_boot+UINT64_C(120000000000)||fx_deadline_utc!=admitted_utc+120000)goto done;
 f.o.admitted_utc_ms=admitted_utc;
 /* The adapter work/retirement sublease ends at original105s; the fixed
  * controller retains original105..120 solely for owned setup retirement.
  * Both derive from the same pre-seal admission, never from case entry. */
 f.o.deadline_utc_ms=admitted_utc+105000;
 f.o.admitted_boot_ns=admitted_boot;
 f.o.deadline_boot_ns=admitted_boot+UINT64_C(105000000000);
 f.o.admission_sha256=view(pins[0]);
 f.o.source_sha256=view(pins[1]);
 f.o.adapter_binary_sha256=view(pins[2]);
 f.o.toolchain_closure_sha256=view(pins[3]);
 f.o.native_profile_sha256=view(pins[4]);
 f.o.expected_input_sha256=view(pins[0]);
 int written=snprintf(root,sizeof root,"%s/fixture-XXXXXX",storage);
 if(written<0||(size_t)written>=sizeof root||!fx_work()||!mkdtemp(root))goto done;
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
 if(!fx_write_work(operation,"pids.max","32\n")||!fx_write_work(operation,"memory.max","536870912\n")||!fx_write_work(operation,"cpu.max","100000 100000\n")||
  !fx_write_work(operation,"cgroup.subtree_control","+pids +memory +cpu\n"))goto done;
 control=fx_child_group(operation,"control");
 requester=fx_child_group(operation,"requester");
 command=fx_child_group(operation,"command");
 if(control<0||requester<0||command<0)goto done;
 char self[32];
 snprintf(self,sizeof self,"%ld\n",(long)getpid());
 if(!fx_write_work(control,"cgroup.procs",self))goto done;
 moved=1;
 if(!fx_step("setup/controller-registration-join",fx_join_root(parent,home,operation,control,requester,command,name),0)){
  fprintf(stderr,"BLOCKED native registration JOIN before child release\n");uncertain=1;goto done;
 }
 if(!fx_step("setup/controller-pipe-clock",fx_work(),0)||!fx_result("setup/controller-ready-pipe",pipe2(ready,O_CLOEXEC),0)||
  !fx_result("setup/controller-mapped-pipe",pipe2(mapped,O_CLOEXEC),0)||!fx_result("setup/controller-prepared-pipe",pipe2(prepared,O_CLOEXEC),0))goto done;
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
 if(!fx_step("setup/controller-keeper-ready",fx_wait_byte(ready[0]),0)||!fx_step("setup/controller-singleton-maps",fx_map(keeper_pid),0)||
  !fx_step("setup/controller-map-ack-clock",fx_work(),0)||!fx_result("setup/controller-map-ack-write",write(mapped[1],"1",1),1)||
  !fx_step("setup/controller-keeper-prepared",fx_wait_byte(prepared[0]),0)){
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
  if(!clocks(&case_utc,&case_boot)||case_boot>=f.o.deadline_boot_ns-f.o.cleanup_reserve_ns||case_utc>=f.o.deadline_utc_ms-f.o.cleanup_reserve_ns/UINT64_C(1000000)||!pidfd_alive(keeper)||!fx_run_case(&f,operation,control,requester,command,user,mount_ns,keeper,executable,supervisor,cwd,
   socket_path,dotnet,copied_dll,root,cases[i],&captured,&uncertain,i)){accepted=0;
   break;
   }
  if(uncertain)break;
 }
 done:
 if(fx_child_open_unknown)uncertain=1;
 /* Preserve original subtree/first cause on any unknown. Do not rerun a case.
  * Actual parent/controller-home handles remain with the outer root profile. */
 if(command>=0){int pop;
  if(!fx_step("cleanup/command-population",populated(command,&pop),0))uncertain=1;
  else if(pop){if(!fx_write(command,"cgroup.kill","1\n"))uncertain=1;
   while(fx_alive()&&pop){if(!populated(command,&pop)){uncertain=1;
     break;
     }struct pollfd p={-1,0,0};
    poll(&p,0,10);
    }if(!fx_step("cleanup/command-empty-before-retirement",!pop,0))uncertain=1;
   }}
 if(keeper>=0){fx_pidfd_kill(keeper);
  if(!fx_step("cleanup/keeper-exit",fx_wait_exit(keeper),0))uncertain=1;
  }
 if(moved){char self[32];
  snprintf(self,sizeof self,"%ld\n",(long)getpid());
  if(!fx_step("cleanup/controller-home-write",fx_write(home,"cgroup.procs",self),0)||
   !fx_step("cleanup/controller-home-readback",membership((long)getpid(),home),0))uncertain=1;
  }
 if(fx_join.registered&&!uncertain&&!fx_step("cleanup/operation-drain-ack",fx_join_drain(8,"operation"),0))uncertain=1;
 int fds[]={user,mount_ns,executable,supervisor,cwd,keeper,ready[0],ready[1],mapped[0],mapped[1],prepared[0],prepared[1],command,requester,control};
 for(unsigned i=0;i<sizeof fds/sizeof fds[0];i++)if(fds[i]>=0)close(fds[i]);
 if(operation>=0&&!uncertain){int pop;
  if(!fx_step("cleanup/operation-population",populated(operation,&pop),0)||!fx_step("cleanup/operation-empty",!pop,0))uncertain=1;
  else if(!fx_step("cleanup/command-remove-clock",fx_alive(),0)||!fx_result("cleanup/command-remove",unlinkat(operation,"command",AT_REMOVEDIR),0)||
   !fx_step("cleanup/requester-remove-clock",fx_alive(),0)||!fx_result("cleanup/requester-remove",unlinkat(operation,"requester",AT_REMOVEDIR),0)||
   !fx_step("cleanup/control-remove-clock",fx_alive(),0)||!fx_result("cleanup/control-remove",unlinkat(operation,"control",AT_REMOVEDIR),0)||
   !fx_step("cleanup/operation-remove-clock",fx_alive(),0)||!fx_result("cleanup/operation-remove",unlinkat(parent,name,AT_REMOVEDIR),0))uncertain=1;
  }
 if(operation>=0)close(operation);
 if(home>=0)close(home);
 if(parent>=0)close(parent);
 if(setup&&!uncertain){if(!fx_alive()||unlink(copied_binary))uncertain=1;
  const char *files[]={"PersistentV3.ImageClosure.Tests.dll","PersistentV3.ImageClosure.Tests.deps.json","PersistentV3.ImageClosure.Tests.runtimeconfig.json","PersistentV3.ImageClosure.dll","PersistentV3.Preparation.dll","FSharp.Core.dll"};
  for(unsigned i=0;i<6&&!uncertain;i++){char path[256];
   int n=snprintf(path,sizeof path,"%s/%s",root,files[i]);
   if(n<0||(size_t)n>=sizeof path||!fx_alive()||unlink(path))uncertain=1;
   }if(!uncertain&&socket_path[0]&&(!fx_alive()||unlink(socket_path)))uncertain=1;
  if(!uncertain&&(!fx_alive()||rmdir(root)))uncertain=1;
  }
 printf("native fixture result=%s captured=%llu cleanup=%s retained-root=%s\n",accepted&&!uncertain?"PASS":"BLOCKED_OR_FAILED",(unsigned long long)captured,uncertain?"UNKNOWN":"CLEANED",uncertain?root:"none");
 return accepted&&!uncertain?0:(uncertain?43:42);
}
/* The sole protected TEST caller creates only its own fresh system.slice child.
 * sudo/PAM/sealing are sampled outer overhead, never claimed inside these limits. */
static int fx_controller_enabled(int fd,const char *file,const char *name) {
 char value[512],*save=NULL;
 if(!read_at(fd,file,value,sizeof value))return 0;
 for(char *word=strtok_r(value," \n",&save);word;word=strtok_r(NULL," \n",&save))
  if(!strcmp(word,name))return 1;
 return 0;
}
static int fx_open_home(void) {
 if(!fx_work())return -1;
 char proc_path[64],value[4096];
 int written=snprintf(proc_path,sizeof proc_path,"/proc/%ld",(long)getpid());
 if(written<0||(size_t)written>=sizeof proc_path)return -1;
 int proc=open(proc_path,O_RDONLY|O_DIRECTORY|O_CLOEXEC|O_NOFOLLOW);
 if(proc<0)return -1;
 int ok=read_at(proc,"cgroup",value,sizeof value);
 close(proc);
 if(!ok||strncmp(value,"0::/",4))return -1;
 char *path=value+4,*newline=strchr(path,'\n');
 if(!newline||newline[1]||newline==path)return -1;
 *newline=0;
 int fd=open("/sys/fs/cgroup",O_RDONLY|O_DIRECTORY|O_CLOEXEC|O_NOFOLLOW);
 if(fd<0)return -1;
 char *save=NULL;
 for(char *part=strtok_r(path,"/",&save);part;part=strtok_r(NULL,"/",&save)){
  if(!fx_work()){close(fd);return -1;}
  if(!strcmp(part,".")||!strcmp(part,"..")){close(fd);return -1;}
  for(const unsigned char *c=(const unsigned char *)part;*c;c++)if(*c<32||*c==127){close(fd);return -1;}
  int next=openat(fd,part,O_RDONLY|O_DIRECTORY|O_CLOEXEC|O_NOFOLLOW);
  close(fd);fd=next;
  if(fd<0)return -1;
 }
 if(!membership((long)getpid(),fd)){close(fd);return -1;}
 return fd;
}
static int fx_seal_mount(const char *path) {
 struct mount_attr attributes;
 memset(&attributes,0,sizeof attributes);
 attributes.attr_set=MOUNT_ATTR_RDONLY;
 return fx_work()&&mount(path,path,NULL,MS_BIND|MS_REC,NULL)==0&&fx_work()&&
  syscall(SYS_mount_setattr,AT_FDCWD,path,AT_RECURSIVE,&attributes,sizeof attributes)==0;
}
static int fx_owned_native(const char *binary,const char *dotnet,const char *dll,
 const char *original_utc,const char *original_boot) {
 uint64_t utc,boot,started_utc,started_boot;
 int anchor=-1,original_home=-1,parent=-1,home=-1,moved=0,storage_created=0,storage_mounted=0;
 int result=42,uncertain=0,inner_ran=0,seal=-1,seal_qualified=0,restored=0;
 char seal_path[256]={0};
 struct stat seal_identity,seal_inputs[7];
 const char *seal_names[]={"controller","PersistentV3.ImageClosure.Tests.dll","PersistentV3.ImageClosure.Tests.deps.json","PersistentV3.ImageClosure.Tests.runtimeconfig.json","PersistentV3.ImageClosure.dll","PersistentV3.Preparation.dll","FSharp.Core.dll"};
 char nonce[65],name[96]={0},storage[]="/tmp/fsgg-learn-component-XXXXXX";
 const char *first=NULL;
 lc_capability anchor_cap={0},original_cap={0},parent_cap={0},home_cap={0};
 if(geteuid()!=0||!fx_decimal(original_utc,&started_utc)||!fx_decimal(original_boot,&started_boot)||
  started_utc>UINT64_MAX-120000||started_boot>UINT64_MAX-UINT64_C(120000000000)){
  fprintf(stderr,"BLOCKED component root/original-clock input\n");return 42;
 }
 fx_started_boot=started_boot;
 fx_started_utc=started_utc;
 fx_deadline=started_boot+UINT64_C(120000000000);
 fx_deadline_utc=started_utc+120000;
 if(!clocks(&utc,&boot)||!fx_original_clock(started_utc,started_boot,utc,boot)||!fx_work()){
  first="original-clock-expired-or-unjoined";goto done;
 }
 const char *pins[]={getenv("FSGG_LEARN_NATIVE_ADMISSION_SHA256"),getenv("FSGG_LEARN_NATIVE_SOURCE_SHA256"),
  getenv("FSGG_LEARN_NATIVE_ADAPTER_SHA256"),getenv("FSGG_LEARN_CUSTODY_CORE_TOOLCHAIN_SHA256"),getenv("FSGG_LEARN_NATIVE_PROFILE_SHA256")};
 for(unsigned i=0;i<5;i++)if(!pins[i]||!digest(view(pins[i]))){first="original-admission-source-profile-join";goto done;}
 /* This private root seal is produced by the reviewed literal, not by an env
  * digest. Root caller/source/profile authentication remains outside this ABI. */
 const char *slash=strrchr(binary,'/');
 if(!slash||strcmp(slash+1,"controller")||strncmp(binary,"/tmp/fsgg-learn-root-seal-",sizeof "/tmp/fsgg-learn-root-seal-"-1)||
  (size_t)(slash-binary)>=sizeof seal_path||strcmp(dotnet,"/usr/share/dotnet/dotnet")){
  first="fixed-root-seal-selection";goto done;
 }
 memcpy(seal_path,binary,(size_t)(slash-binary));seal_path[slash-binary]=0;
 seal=open(seal_path,O_RDONLY|O_DIRECTORY|O_CLOEXEC|O_NOFOLLOW);
 if(seal<0||fstat(seal,&seal_identity)||!S_ISDIR(seal_identity.st_mode)||seal_identity.st_uid!=0||(seal_identity.st_mode&0777)!=0700){
  first="root-seal-owned-directory";goto done;
 }
 for(unsigned i=0;i<7;i++)if(fstatat(seal,seal_names[i],&seal_inputs[i],AT_SYMLINK_NOFOLLOW)||
  !S_ISREG(seal_inputs[i].st_mode)||seal_inputs[i].st_uid!=0||
  (seal_inputs[i].st_mode&0777)!=(i?0444:0555)){
  first="root-seal-fixed-file-custody";goto done;
 }
 char sealed_dll[512];
 int selected=snprintf(sealed_dll,sizeof sealed_dll,"%s/%s",seal_path,seal_names[1]);
 if(selected<0||(size_t)selected>=sizeof sealed_dll||strcmp(dll,sealed_dll)){first="root-seal-fixed-dll-selection";goto done;}
 seal_qualified=1;
 if(!fx_join_init()){first="registration-socket-observer-birth";goto done;}
 anchor=open("/sys/fs/cgroup/system.slice",O_RDONLY|O_DIRECTORY|O_CLOEXEC|O_NOFOLLOW);
 original_home=fx_open_home();
 if(anchor<0||original_home<0||!fx_cap(&anchor_cap,LC_OPERATION_CGROUP,UINT32_MAX,anchor)||
  !fx_cap(&original_cap,LC_CONTROL_LEAF,UINT32_MAX,original_home)||!verify_cap(&anchor_cap)||
  !verify_cap(&original_cap)||!ancestor(original_home,anchor)||!membership((long)getpid(),original_home)){
  first="fresh-anchor-home-mount-type-membership";goto done;
 }
 const char *controllers[]={"cpu","memory","pids"};
 for(unsigned i=0;i<3;i++)if(!fx_controller_enabled(anchor,"cgroup.controllers",controllers[i])||
  !fx_controller_enabled(anchor,"cgroup.subtree_control",controllers[i])){
  first="shared-anchor-controller-not-already-enabled";goto done;
 }
 memcpy(nonce,fx_join.nonce,sizeof nonce);
 int written=snprintf(name,sizeof name,"fsgg-learn-component-%.32s",nonce);
 if(written<0||(size_t)written>=sizeof name||!verify_cap(&anchor_cap)||!verify_cap(&original_cap)||
  !membership((long)getpid(),original_home)||!fx_work()){
  first="pre-create-original-capability-or-clock";goto done;
 }
 memcpy(fx_join.root_name,name,strlen(name)+1);
 parent=fx_child_group(anchor,name);
 if(parent<0){first="exclusive-child-create-or-adopt";goto done;}
 if(!fx_cap(&parent_cap,LC_OPERATION_CGROUP,UINT32_MAX,parent)||!verify_cap(&parent_cap)||!ancestor(parent,anchor)||
  !file_equal(parent,"cgroup.procs","")){
  first="new-empty-owned-domain-join";goto done;
 }
 /* No write to the shared anchor, service controls, or any foreign process. */
 if(!fx_write_work(parent,"pids.max","32\n")||!fx_write_work(parent,"memory.max","536870912\n")||
  !fx_write_work(parent,"cpu.max","100000 100000\n")||!fx_write_work(parent,"cgroup.subtree_control","+cpu +memory +pids\n")){
  first="owned-parent-hard-limits";goto done;
 }
 home=fx_child_group(parent,"home");
 if(home<0||!fx_cap(&home_cap,LC_CONTROL_LEAF,UINT32_MAX,home)||!verify_cap(&home_cap)||!ancestor(home,parent)){
  first="owned-home-join";goto done;
 }
 char self[32];
 written=snprintf(self,sizeof self,"%ld\n",(long)getpid());
 if(written<0||(size_t)written>=sizeof self||!verify_cap(&original_cap)||!membership((long)getpid(),original_home)||
  !fx_write_work(home,"cgroup.procs",self)){
  first="controller-self-enrollment";goto done;
 }
 moved=1; /* A failed readback still requires restoration of this exact self. */
 if(!membership((long)getpid(),home)||!verify_cap(&parent_cap)||!verify_cap(&home_cap)||!fx_work()){
  first="enrollment-readback";goto done;
 }
 printf("component inner enrollment=JOINED parent-dev=%llu parent-inode=%llu mount=%llu owned-name=%s start-utc=%llu start-boot=%llu deadline-boot=%llu\n",
  (unsigned long long)parent_cap.device,(unsigned long long)parent_cap.inode,(unsigned long long)parent_cap.mount_identity,name,
  (unsigned long long)started_utc,(unsigned long long)started_boot,(unsigned long long)fx_deadline);
 if(fflush(stdout)){first="enrollment-evidence-write";goto done;}
 /* Controller is inside its hard boundary before namespace/fixture writes.
  * The root seal input is separately byte-bounded outer setup. Each region is
  * separately bounded:64MiB seal +62MiB fixture +two1MiB keeper
  * tmpfs mounts stay<=128MiB file-data capacity. Kernel/inode metadata is
  * inner memory charge; provider root-seal metadata is sampled outer. */
 if(!fx_step("setup/outer-private-mount-clock",fx_work(),0)||!fx_result("setup/outer-private-mountns",unshare(CLONE_NEWNS),0)||
  !fx_step("setup/outer-private-root-clock",fx_work(),0)||!fx_result("setup/outer-private-root",mount(NULL,"/",NULL,MS_PRIVATE|MS_REC,NULL),0)||
  !fx_step("setup/outer-usr-seal",fx_seal_mount("/usr"),0)||!fx_step("setup/outer-etc-seal",fx_seal_mount("/etc"),0)){
  first="private-immutable-visible-runtime-closure";goto done;
 }
 if(!fx_step("setup/outer-storage-clock",fx_work(),0)||!fx_step("setup/outer-storage-create",mkdtemp(storage)!=NULL,1)){first="owned-fixture-storage-create";goto done;}
 storage_created=1;
 if(!fx_step("setup/outer-storage-mount-clock",fx_work(),0)||!fx_result("setup/outer-storage-tmpfs",mount("tmpfs",storage,"tmpfs",MS_NOSUID|MS_NODEV,"size=65011712,mode=0755"),0)){
  first="hard-fixture-disk-before-write";goto done;
 }
 storage_mounted=1;
 inner_ran=1;
 result=fx_native(binary,dotnet,dll,parent,home,storage,started_utc,started_boot);
 if(result!=0)first="inner-fixture-blocked-failed-or-unknown";
 if(result==43)uncertain=1;
 done:
 /* The root literal already created the seal before exec. If adoption never
  * completed, its retirement is unknown even when no cgroup was created. */
 if(fx_child_open_unknown||!seal_qualified)uncertain=1;
 /* Restore only this controller to its retained original home. Never move or
  * signal privileged ancestors, provider service tasks, or unrelated clients.
  * Hard inner accounting ends at successful self-restoration. Subsequent
  * storage/name/seal retirement is sampled outer custody, not hard-contained. */
 if(moved){char self[32];
  int written=snprintf(self,sizeof self,"%ld\n",(long)getpid());
  if(!fx_step("cleanup/outer-home-identity-format",written>=0&&(size_t)written<sizeof self,0)||
   !fx_step("cleanup/outer-home-capability",verify_cap(&original_cap),0)||
   !fx_step("cleanup/outer-home-write",fx_write(original_home,"cgroup.procs",self),0)||
   !fx_step("cleanup/outer-home-readback",membership((long)getpid(),original_home),0))uncertain=1;
  else restored=1;
 }
 if(parent>=0){int pop=1;
  if(!fx_step("cleanup/outer-parent-capability",verify_cap(&parent_cap),0)||
   !fx_step("cleanup/outer-parent-population",populated(parent,&pop),0)||!fx_step("cleanup/outer-parent-empty",!pop,0))uncertain=1;
 }
 if(home>=0&&!fx_step("cleanup/outer-home-capability-terminal",verify_cap(&home_cap),0))uncertain=1;
 if(fx_join.registered&&!uncertain&&!fx_step("cleanup/aggregate-drain-ack",fx_join_drain(8,"aggregate"),0))uncertain=1;
 if(!fx_step("cleanup/outer-original-clock",fx_alive(),0))uncertain=1;
 if(!uncertain&&storage_mounted&&(!fx_step("cleanup/outer-storage-unmount-clock",fx_alive(),0)||!fx_result("cleanup/outer-storage-unmount",umount2(storage,0),0)))uncertain=1;
 if(!uncertain&&storage_created&&(!fx_step("cleanup/outer-storage-remove-clock",fx_alive(),0)||!fx_result("cleanup/outer-storage-remove",rmdir(storage),0)))uncertain=1;
 if(!uncertain&&home>=0&&(!fx_step("cleanup/outer-home-remove-clock",fx_alive(),0)||!fx_result("cleanup/outer-home-remove",unlinkat(parent,"home",AT_REMOVEDIR),0)))uncertain=1;
 if(!uncertain&&parent>=0&&(!fx_step("cleanup/outer-parent-remove-clock",fx_alive(),0)||!fx_step("cleanup/outer-anchor-capability",verify_cap(&anchor_cap),0)||
  !fx_result("cleanup/outer-parent-remove",unlinkat(anchor,name,AT_REMOVEDIR),0)))uncertain=1;
 /* Remove only the original fixed seal files after both inner and setup
  * cleanup are known. An unjoined seal/extra file is retained, never rm-rf'd. */
 if(seal>=0&&!uncertain){struct stat current;
  if(!fx_result("cleanup/outer-seal-stat",fstat(seal,&current),0)||
   !fx_step("cleanup/outer-seal-identity",current.st_dev==seal_identity.st_dev&&current.st_ino==seal_identity.st_ino,0))uncertain=1;
  for(unsigned i=0;i<7&&!uncertain;i++)if(!fx_result("cleanup/outer-seal-input-stat",fstatat(seal,seal_names[i],&current,AT_SYMLINK_NOFOLLOW),0)||
   !fx_step("cleanup/outer-seal-input-identity",current.st_dev==seal_inputs[i].st_dev&&current.st_ino==seal_inputs[i].st_ino&&current.st_uid==0,0))uncertain=1;
  if(!uncertain){for(unsigned i=0;i<7&&!uncertain;i++)if(!fx_step("cleanup/outer-seal-input-remove-clock",fx_alive(),0)||
    !fx_result("cleanup/outer-seal-input-remove",unlinkat(seal,seal_names[i],0),0))uncertain=1;
   if(!uncertain&&(!fx_step("cleanup/outer-seal-remove-clock",fx_alive(),0)||!fx_result("cleanup/outer-seal-remove",rmdir(seal_path),0)))uncertain=1;
  }
 }
 if(!clocks(&utc,&boot)||boot>=fx_deadline||utc>=fx_deadline_utc){
  uncertain=1;if(!first)first="original-deadline-at-terminal";
 }
 if(fx_join.observer_pidfd>=0)close(fx_join.observer_pidfd);
 close(STDIN_FILENO);
 if(seal>=0)close(seal);
 if(home>=0)close(home);
 if(parent>=0)close(parent);
 if(original_home>=0)close(original_home);
 if(anchor>=0)close(anchor);
 printf("component inner-result=%s first-cause=%s cleanup=%s outer-wrapper-retirement=UNKNOWN hard-inner-end=%s post-restoration-custody=SAMPLED_OUTER inner-ran=%s retained-owned-parent=%s retained-storage=%s retained-seal=%s\n",
  result==0&&!uncertain?"PASS":"BLOCKED_OR_FAILED",first?first:"none",uncertain?"UNKNOWN":"CLEANED",restored?"self-restoration":(moved?"UNKNOWN":"NOT_ENROLLED"),inner_ran?"true":"false",
  uncertain&&name[0]?name:"none",uncertain&&storage_created?storage:"none",uncertain?(seal_path[0]?seal_path:binary):"none");
 return uncertain?43:result;
}

int main(int argc,char **argv) {
 if(argc==3&&!strcmp(argv[1],"--custody-fixture-workload"))return fx_workload(argv[2]);
 if(argc==6&&!strcmp(argv[1],"--native-fixture"))return fx_owned_native(argv[0],argv[2],argv[3],argv[4],argv[5]);
 if(argc!=1){fprintf(stderr,"focused custody source/native fixture arguments refused\n");
  return 2;
  }
 return fsgg_custody_adapter_controls();
}
#endif
