/* Focused test translation unit; its conventional test entry may run only after
 * admitted compilation. No transport, child process or native retirement. All
 * cc_result observations below are explicitly synthetic. */
#include "../../deployment/telemetry-collector/persistent/v3/image-closure/native/custody_core.h"
#include <limits.h>
#include <stdio.h>
#include <string.h>

_Static_assert(sizeof(int)==4 && sizeof(size_t)==8 && sizeof(void *)==8,"selected source fixture requires 64-bit C ABI and 32-bit int");

#define H "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
#define A "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
#define I "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"
#define L "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd"
#define OTHER "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee"
#define BIND "{\"input\":\"" I "\",\"admissionSha256\":\"" A "\"}"
#define START "{\"Executable\":\"/usr/sbin/podman\",\"Arguments\":[\"build\",\"--network=none\"],\"Environment\":{\"HOME\":\"/private/empty\",\"LANG\":\"C\"},\"WorkingDirectory\":\"/private/work\",\"OutputLimit\":1048576}"
#define LEASE "{\"lease\":\"" L "\"}"
static unsigned char frame[CC_FRAME_MAX], text[CC_FRAME_MAX], wire[CC_FRAME_MAX+4], reply[CC_FRAME_MAX+4], output[CC_OUTPUT_MAX];
static cc_node nodes[4096];
static cc_lease leases[8];
static cc_core core;
static int failures, checks;
static size_t last_reply_size;
static cc_view v(const char *s) { cc_view x; x.data=(const unsigned char *)s; x.size=strlen(s); return x; }
static cc_view bytes(const void *s,size_t n) { cc_view x; x.data=s; x.size=n; return x; }
static void check(int ok,const char *name) { checks++; if (!ok) { failures++; fprintf(stderr,"FAIL custody-core %s\n",name); } }
static int fresh(uint64_t allowance) {
    cc_workspace w; w.frame=frame; w.frame_capacity=sizeof frame; w.text=text; w.text_capacity=sizeof text; w.nodes=nodes; w.node_capacity=sizeof nodes/sizeof nodes[0];
    return cc_init(&core,w,leases,8,v(H),v(A),v(I),1000,2000,allowance);
}
static size_t framed(const char *s) {
    size_t n=strlen(s); if (n>CC_FRAME_MAX) return 0;
    wire[0]=(unsigned char)(n>>24); wire[1]=(unsigned char)(n>>16); wire[2]=(unsigned char)(n>>8); wire[3]=(unsigned char)n;
    memcpy(wire+4,s,n); return n+4;
}
static cc_progress json_request(const char *action,const char *payload,int ordinal) {
    int n=snprintf((char *)wire+4,sizeof wire-4,"{\"schema\":\"" CC_SCHEMA "\",\"identity\":\"" H "\",\"ordinal\":%d,\"action\":\"%s\",\"payload\":%s}",ordinal,action,payload);
    if (n<0 || (size_t)n>=sizeof wire-4) return CC_FAILED;
    wire[0]=(unsigned char)((unsigned)n>>24); wire[1]=(unsigned char)((unsigned)n>>16); wire[2]=(unsigned char)((unsigned)n>>8); wire[3]=(unsigned char)n;
    return cc_feed(&core,wire,(size_t)n+4,1001);
}
static cc_result synthetic(void) { cc_result r; memset(&r,0,sizeof r); r.accepted=1; r.lease=v(L); r.stdout_bytes=v(""); r.stderr_bytes=v(""); return r; }
static int complete(cc_result *r) { return cc_complete(&core,r,1001,reply,sizeof reply,&last_reply_size); }
static int reply_contains(const char *s) {
    size_t i,n=strlen(s);
    for (i=4;i<=last_reply_size && n<=last_reply_size-i;i++) if (!memcmp(reply+i,s,n)) return 1;
    return 0;
}
static int bound(void) { cc_result r=synthetic(); return json_request("bind-input",BIND,1)==CC_READY && complete(&r); }
static int started(void) { cc_result r=synthetic(); return bound() && json_request("start",START,2)==CC_READY && complete(&r); }
static void refuses(const char *action,const char *payload,int ordinal,const char *name) {
    check(json_request(action,payload,ordinal)==CC_FAILED && core.poisoned,name);
    check(json_request("retain","{\"bytes\":0}",ordinal)==CC_FAILED && !cc_current(&core),"poison prevents reuse");
}
static void positive_wire(void) {
    cc_result r=synthetic(); size_t n,p,cmd,args,env; const cc_request *q;
    const char *expected="{\"schema\":\"" CC_SCHEMA "\",\"identity\":\"" H "\",\"ordinal\":1,\"status\":\"acknowledged\",\"payload\":{\"input\":\"" I "\"}}";
    check(fresh(UINT64_MAX),"init");
    n=framed("{\"payload\":{\"admissionSha256\":\"" A "\",\"input\":\"" I "\"},\"action\":\"bind-input\",\"ordinal\":1,\"identity\":\"" H "\",\"schema\":\"" CC_SCHEMA "\"}");
    for (p=0;p<n;p++) check(cc_feed(&core,wire+p,1,1001)==(p+1==n?CC_READY:CC_MORE),"fragmented header/body");
    check(!core.bound && core.ordinal==0,"receipt does not acknowledge binding");
    check(cc_complete(&core,&r,1001,reply,sizeof reply,&p),"binding adapter result");
    check(p==strlen(expected)+4 && !memcmp(reply+4,expected,strlen(expected)),"exact F# binding reply");
    check(json_request("start",START,2)==CC_READY,"PascalCase start wire");
    q=cc_current(&core); cmd=q?q->command:CC_NONE;
    args=cc_field(&core,cmd,"Arguments"); env=cc_field(&core,cmd,"Environment");
    check(cc_get_node(&core,args) && cc_get_node(&core,args)->type==CC_ARRAY && cc_field(&core,env,"HOME")!=CC_NONE,"actual command collections exposed");
    check(core.lease_count==0 && !cc_cached_retirement(&core,v(L),1001),"no synthetic start from receipt");
    /* cached foreign lease fails closed, so reconstruct before explicit result. */
    check(core.poisoned,"foreign cached lease poisons");
    check(fresh(100) && started(),"explicit synthetic start");
    check(json_request("observe",LEASE,3)==CC_READY,"observe wire");
    r.stdout_bytes=v("a\n\"\\\xC3\xA9"); r.stderr_bytes=v("z"); r.exit_code=-7;
    check(complete(&r) && !cc_cached_retirement(&core,v(L),1001),"unretired synthetic result stays unretired");
    check(reply_contains("\"retired\":false,\"exitCode\":-7,\"stdout\":\"a\\u000a\\\"\\\\\xC3\xA9\",\"stderr\":\"z\""),"escaped observation acknowledgment fields");
    check(json_request("cancel",LEASE,4)==CC_READY,"cancel wire"); r.retired=1;
    check(complete(&r) && cc_cached_retirement(&core,v(L),1001),"explicit synthetic retirement only");
    check(core.charged==r.stdout_bytes.size+r.stderr_bytes.size,"cumulative output not double charged");
    cc_poison(&core,"synthetic-write-loss");
    check(!cc_cached_retirement(&core,v(L),1001),"cached retirement refuses after transport loss");
}
static void invalid_wire(void) {
    static const char *const envelopes[]={
      "{\"schema\":\"wrong\",\"identity\":\"" H "\",\"ordinal\":1,\"action\":\"retain\",\"payload\":{\"bytes\":0}}",
      "{\"schema\":\"" CC_SCHEMA "\",\"identity\":\"" OTHER "\",\"ordinal\":1,\"action\":\"retain\",\"payload\":{\"bytes\":0}}",
      "{\"schema\":\"" CC_SCHEMA "\",\"identity\":\"" H "\",\"ordinal\":true,\"action\":\"retain\",\"payload\":{\"bytes\":0}}",
      "{\"schema\":\"" CC_SCHEMA "\",\"identity\":\"" H "\",\"ordinal\":2147483648,\"action\":\"retain\",\"payload\":{\"bytes\":0}}",
      "{\"schema\":\"" CC_SCHEMA "\",\"identity\":\"" H "\",\"ordinal\":1,\"ordinal\":1,\"action\":\"retain\",\"payload\":{\"bytes\":0}}"
    };
    static const struct { const char *action,*payload,*name; int setup,ordinal; } cases[]={
      {"start",START,"start before binding",0,1},
      {"bind-input",BIND,"bind twice",1,2},
      {"bind-input","{\"input\":\"" I "\",\"admissionSha256\":\"" OTHER "\"}","foreign admission",0,1},
      {"bind-input","{\"input\":\"BAD\",\"admissionSha256\":\"" A "\"}","invalid input digest",0,1},
      {"bind-input","{\"input\":\"" OTHER "\",\"admissionSha256\":\"" A "\"}","foreign expected input",0,1},
      {"retain","{\"bytes\":-1}","negative retain",0,1},
      {"retain","{\"bytes\":2147483648}","int32 retain overflow",0,1},
      {"retain","{\"bytes\":9223372036854775808}","int64 parser overflow",0,1},
      {"retain","{\"bytes\":1.0}","fractional number",0,1},
      {"retain","{\"bytes\":01}","leading zero",0,1},
      {"retain","{\"bytes\":true}","wrong scalar type",0,1},
      {"retain","{\"bytes\":null}","null scalar",0,1},
      {"retain","{\"bytes\":0,\"bytes\":0}","duplicate key",0,1},
      {"retain","{\"bytes\":0,\"by\\u0074es\":0}","escaped equivalent key",0,1},
      {"retain","{\"bytes\":0,\"extra\":0}","unexpected field",0,1},
      {"retain","{}","missing field",0,1},
      {"retain","{\"bytes\":0}","wrong ordinal",0,2},
      {"observe","{\"lease\":\"\"}","empty lease",2,3},
      {"cancel","{\"lease\":\"" OTHER "\"}","foreign lease",2,3},
      {"bogus","{}","unknown action",0,1},
      {"start","{\"Executable\":\"/usr/sbin/podman\",\"Arguments\":[],\"Environment\":{\"A\":\"a\",\"\\u0041\":\"b\"},\"WorkingDirectory\":\"/w\",\"OutputLimit\":1048576}","duplicate environment key",1,2},
      {"start","{\"Executable\":\"/usr/sbin/podman\",\"Arguments\":[1],\"Environment\":{},\"WorkingDirectory\":\"/w\",\"OutputLimit\":1048576}","nonstring argument",1,2},
      {"start","{\"Executable\":\"/usr/sbin/podman\",\"Arguments\":[],\"Environment\":{},\"WorkingDirectory\":\"\\uD800\",\"OutputLimit\":1048576}","unpaired surrogate",1,2},
      {"start","{\"Executable\":\"/usr/sbin/podman\",\"Arguments\":[],\"Environment\":{},\"WorkingDirectory\":\"\xC0\xAF\",\"OutputLimit\":1048576}","overlong UTF8",1,2},
      {"start","{\"executable\":\"/usr/sbin/podman\",\"Arguments\":[],\"Environment\":{},\"WorkingDirectory\":\"/w\",\"OutputLimit\":1048576}","wrong PascalCase field",1,2}
    };
    size_t i;
    for (i=0;i<sizeof envelopes/sizeof envelopes[0];i++) {
        size_t n; check(fresh(UINT64_MAX),"independent invalid envelope setup"); n=framed(envelopes[i]);
        check(cc_feed(&core,wire,n,1001)==CC_FAILED && core.poisoned,"schema/identity/type/ordinal/duplicate envelope refuses");
    }
    for (i=0;i<sizeof cases/sizeof cases[0];i++) {
        check(fresh(UINT64_MAX),"fresh independent negative case");
        if (cases[i].setup) check(cases[i].setup==1?bound():started(),"negative prerequisite binding/start");
        refuses(cases[i].action,cases[i].payload,cases[i].ordinal,cases[i].name);
    }
    check(fresh(100) && bound(),"unicode prerequisite");
    check(json_request("start","{\"Executable\":\"/usr/sbin/podman\",\"Arguments\":[\"\\uD83D\\uDE00\",\"\\u0000\"],\"Environment\":{},\"WorkingDirectory\":\"/w\",\"OutputLimit\":1048576}",2)==CC_READY,"surrogate pair and embedded NUL preserved");
    {
        const cc_request *q=cc_current(&core); const cc_node *a=q?cc_get_node(&core,cc_field(&core,q->command,"Arguments")):NULL;
        const cc_node *first=a?cc_get_node(&core,a->child):NULL,*second=first?cc_get_node(&core,first->next):NULL;
        check(first && second && first->text.size==4 && first->text.data[0]==0xf0 && second->text.size==1 && second->text.data[0]==0,"decoded Unicode lengths");
    }
}
static void framing_and_deadline(void) {
    size_t n,written; unsigned char h[4]={0,0,0,0}; cc_result r=synthetic();
    check(fresh(100),"frame zero setup"); check(cc_feed(&core,h,4,1001)==CC_FAILED,"zero frame");
    check(fresh(100),"oversize setup"); h[0]=0;h[1]=0x20;h[2]=0;h[3]=1;
    check(cc_feed(&core,h,4,1001)==CC_FAILED,"oversize frame");
    check(fresh(100),"truncated header setup"); check(cc_feed(&core,h,3,1001)==CC_MORE && !cc_finish_input(&core) && core.poisoned,"truncated header EOF");
    check(fresh(100),"truncated body setup"); n=framed("{}");
    check(cc_feed(&core,wire,n-1,1001)==CC_MORE && !cc_finish_input(&core),"truncated body EOF");
    check(fresh(100),"surplus frame setup"); n=framed("{}"); wire[n]=0;
    check(cc_feed(&core,wire,n+1,1001)==CC_FAILED,"surplus framed data");
    check(fresh(100),"trailing JSON setup"); n=framed("{} {}");
    check(cc_feed(&core,wire,n,1001)==CC_FAILED,"trailing JSON");
    check(fresh(100),"exact frame ceiling setup"); n=framed("{\"schema\":\"" CC_SCHEMA "\",\"identity\":\"" H "\",\"ordinal\":1,\"action\":\"retain\",\"payload\":{\"bytes\":0}}");
    memset(wire+n,' ',sizeof wire-n); wire[0]=0;wire[1]=0x20;wire[2]=0;wire[3]=0;
    check(cc_feed(&core,wire,sizeof wire,1001)==CC_READY && complete(&r),"exact 2MiB framed JSON ceiling");
    check(fresh(100) && json_request("bind-input",BIND,1)==CC_READY,"deadline pending request");
    check(!cc_complete(&core,&r,2000,reply,sizeof reply,&written) && !core.bound && core.poisoned,"completion cannot renew original deadline");
    check(fresh(100),"deadline feed setup"); n=framed("{}");
    check(cc_feed(&core,wire,n,2000)==CC_FAILED,"deadline feed expiry");
    check(fresh(100),"ordinal cap setup"); core.ordinal=INT32_MAX;
    refuses("retain","{\"bytes\":0}",INT32_MAX,"ordinal cannot wrap");
    check(fresh(100) && json_request("bind-input",BIND,1)==CC_READY,"pending operation setup");
    check(cc_feed(&core,NULL,0,1001)==CC_FAILED && !complete(&r),"pending operation cannot be overwritten");
}
static void result_and_budget(void) {
    cc_result r=synthetic(); size_t written; int i;
    check(fresh(3),"aggregate retain setup");
    check(json_request("retain","{\"bytes\":3}",1)==CC_READY && complete(&r) && core.charged==3,"exact aggregate retain");
    refuses("retain","{\"bytes\":1}",2,"over aggregate retain");
    check(fresh(UINT64_MAX),"overflow-safe aggregate setup"); core.charged=UINT64_MAX-1;
    refuses("retain","{\"bytes\":2}",1,"aggregate addition cannot overflow");
    check(fresh(3) && started(),"output plus retain setup");
    check(json_request("retain","{\"bytes\":1}",3)==CC_READY && complete(&r),"retained evidence shares budget");
    check(json_request("observe",LEASE,4)==CC_READY,"combined accounting observe"); r.stdout_bytes=v("a");r.stderr_bytes=v("b");
    check(complete(&r) && core.charged==3,"exact shared aggregate output");
    check(json_request("cancel",LEASE,5)==CC_READY,"over aggregate cancel");r.stderr_bytes=v("bc");r.retired=1;
    check(!complete(&r) && !core.leases[0].retired && core.charged==3,"over aggregate cannot commit retirement");
    r=synthetic(); memset(output,'x',sizeof output); output[CC_OUTPUT_MAX-2]=0xc3;output[CC_OUTPUT_MAX-1]=0xa9;
    check(fresh(CC_OUTPUT_MAX) && started() && json_request("observe",LEASE,3)==CC_READY,"multibyte boundary setup");
    r.stdout_bytes=bytes(output,CC_OUTPUT_MAX-2);r.stderr_bytes=bytes(output+CC_OUTPUT_MAX-2,2);r.retired=1;
    check(complete(&r) && core.charged==CC_OUTPUT_MAX,"combined exact UTF8 byte boundary");
    check(fresh(UINT64_MAX) && started() && json_request("cancel",LEASE,3)==CC_READY,"combined over setup");
    r.stdout_bytes=bytes(output,CC_OUTPUT_MAX);r.stderr_bytes=v("x");
    check(!complete(&r) && core.poisoned && !core.leases[0].retired,"individually legal streams exceed combined ceiling");
    check(fresh(UINT64_MAX) && started() && json_request("observe",LEASE,3)==CC_READY,"escaped frame setup");
    memset(output,0,sizeof output);r.stdout_bytes=bytes(output,sizeof output);r.stderr_bytes=v("");
    check(!cc_complete(&core,&r,1001,reply,sizeof reply,&written) && written==0 && !core.leases[0].retired && core.charged==0,"decoded legal output exceeds escaped frame");
    for (i=0;i<5;i++) {
        r=synthetic();check(fresh(UINT64_MAX),"malformed explicit result independent setup");
        if (i==0) check(json_request("bind-input",BIND,1)==CC_READY,"bind malformed result setup");
        if (i==1) check(json_request("retain","{\"bytes\":0}",1)==CC_READY,"retain malformed result setup");
        if (i==2) check(bound() && json_request("start",START,2)==CC_READY,"start malformed result setup");
        if (i>=3) check(started() && json_request(i==3?"observe":"cancel",LEASE,3)==CC_READY,"observe/cancel malformed result setup");
        r.accepted=0;check(!complete(&r) && core.poisoned,"malformed result poisons every action");
        check(json_request("retain","{\"bytes\":0}",1)==CC_FAILED,"result poison refuses next operation");
    }
    r=synthetic();check(fresh(UINT64_MAX) && started(),"reuse lease setup");
    check(json_request("start",START,3)==CC_READY && !complete(&r) && core.lease_count==1,"reused start lease poisons");
    check(fresh(UINT64_MAX) && started() && json_request("observe",LEASE,3)==CC_READY,"foreign result setup");r.lease=v(OTHER);
    check(!complete(&r),"foreign result lease refuses");
    r=synthetic();r.retired=2;
    check(fresh(UINT64_MAX) && started() && json_request("cancel",LEASE,3)==CC_READY && !complete(&r),"nonboolean retired input refuses");
    r=synthetic();r.stdout_bytes=bytes(NULL,1);
    check(fresh(UINT64_MAX) && started() && json_request("observe",LEASE,3)==CC_READY && !complete(&r),"null nonempty output refuses");
    r=synthetic();r.stdout_bytes=bytes("\xED\xA0\x80",3);
    check(fresh(UINT64_MAX) && started() && json_request("observe",LEASE,3)==CC_READY && !complete(&r),"UTF8 surrogate output refuses");
    r=synthetic();r.stdout_bytes=v("abc");
    check(fresh(UINT64_MAX) && started() && json_request("observe",LEASE,3)==CC_READY && complete(&r),"cumulative output setup");
    r.stdout_bytes=v("ab");
    check(json_request("observe",LEASE,4)==CC_READY && !complete(&r) && core.charged==3,"cumulative output cannot reduce prior charge");
    r=synthetic();r.retired=1;
    check(fresh(UINT64_MAX) && started() && json_request("observe",LEASE,3)==CC_READY && complete(&r),"cached state prerequisite synthetic retirement");
    refuses("retain","{\"bytes\":-1}",4,"other action poisons cached success");
    check(!cc_cached_retirement(&core,v(L),1001),"cached retirement rejects shared poison");
    r=synthetic();
    check(fresh(UINT64_MAX) && started() && json_request("observe",LEASE,3)==CC_READY,"small reply storage setup");
    check(!cc_complete(&core,&r,1001,reply,4,&written) && !written,"insufficient reply storage cannot commit");
}
int fsgg_custody_core_tests(void) {
    failures=checks=0;
    positive_wire(); invalid_wire(); framing_and_deadline(); result_and_budget();
    fprintf(stderr,"custody-core synthetic semantic checks=%d failures=%d; native retirement unqualified\n",checks,failures);
    return failures;
}
/* Test-only F# bridge. The existing peer owns sockets/tasks; it passes explicit
 * synthetic result values. These functions never receive descriptors or effects. */
int fsgg_custody_core_fixture_reset(const unsigned char *input,size_t input_size,uint64_t now,uint64_t deadline) {
    cc_workspace w;
    w.frame=frame;w.frame_capacity=sizeof frame;w.text=text;w.text_capacity=sizeof text;
    w.nodes=nodes;w.node_capacity=sizeof nodes/sizeof nodes[0];
    return cc_init(&core,w,leases,8,v(H),v(A),bytes(input,input_size),now,deadline,CC_OUTPUT_MAX+4096u);
}
int fsgg_custody_core_fixture_receive(const unsigned char *request,size_t count,uint64_t now) {
    return (int)cc_feed(&core,request,count,now);
}
int fsgg_custody_core_fixture_complete(int accepted,int retired,int exit_code,
        const unsigned char *out,size_t out_size,const unsigned char *err,size_t err_size,
        uint64_t now,unsigned char *response,size_t capacity) {
    cc_result r=synthetic();size_t written;
    r.accepted=accepted;r.retired=retired;r.exit_code=exit_code;
    r.stdout_bytes=bytes(out,out_size);r.stderr_bytes=bytes(err,err_size);
    if (!cc_complete(&core,&r,now,response,capacity,&written)) return -1;
    return (int)written;
}
#ifndef CC_TEST_LIBRARY
int main(void) { return fsgg_custody_core_tests()?1:0; }
#endif
