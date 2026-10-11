#include "custody_core.h"
#include <limits.h>
#include <string.h>

static cc_view view(const void *p, size_t n) { cc_view v; v.data=p; v.size=n; return v; }
static int equal(cc_view a, cc_view b) {
    return a.size==b.size && (!a.size || (a.data && b.data && !memcmp(a.data,b.data,a.size)));
}
static int literal(cc_view v, const char *s) { return equal(v,view(s,strlen(s))); }
static int digest(cc_view v) {
    size_t i;
    if (!v.data || v.size!=64) return 0;
    for (i=0;i<64;i++) if (!((v.data[i]>='0'&&v.data[i]<='9') || (v.data[i]>='a'&&v.data[i]<='f'))) return 0;
    return 1;
}
void cc_poison(cc_core *c, const char *reason) {
    if (!c->poisoned) c->failure=reason;
    c->poisoned=1;
}
static int fail(cc_core *c, const char *reason) { cc_poison(c,reason); return 0; }
static int alive(cc_core *c, uint64_t now) {
    return !c->poisoned && (now<c->deadline_ms || fail(c,"original-deadline"));
}
/* A scalar decoder rejects overlong encodings, surrogates, stray continuation
 * bytes and values above U+10FFFF. Embedded NUL is valid in length-bearing views. */
static int scalar(const unsigned char *p, size_t n, size_t *used, uint32_t *value) {
    uint32_t x, minimum; size_t count,i;
    if (!n || !p) return 0;
    if (p[0]<0x80) { *used=1; *value=p[0]; return 1; }
    if (p[0]>=0xc2 && p[0]<=0xdf) { count=2; x=p[0]&31; minimum=0x80; }
    else if (p[0]>=0xe0 && p[0]<=0xef) { count=3; x=p[0]&15; minimum=0x800; }
    else if (p[0]>=0xf0 && p[0]<=0xf4) { count=4; x=p[0]&7; minimum=0x10000; }
    else return 0;
    if (n<count) return 0;
    for (i=1;i<count;i++) { if ((p[i]&0xc0)!=0x80) return 0; x=(x<<6)|(p[i]&63); }
    if (x<minimum || x>0x10ffff || (x>=0xd800 && x<=0xdfff)) return 0;
    *used=count; *value=x; return 1;
}
static int utf8(cc_view v) {
    size_t i=0,k; uint32_t x;
    if (v.size && !v.data) return 0;
    while (i<v.size) { if (!scalar(v.data+i,v.size-i,&k,&x)) return 0; i+=k; }
    return 1;
}
typedef struct { cc_core *c; size_t at; } parser;
static void ws(parser *p) {
    const unsigned char *s=p->c->workspace.frame;
    while (p->at<p->c->frame_size && (s[p->at]==' ' || s[p->at]=='\t' || s[p->at]=='\n' || s[p->at]=='\r')) p->at++;
}
static int take(parser *p, unsigned char ch) {
    if (p->at<p->c->frame_size && p->c->workspace.frame[p->at]==ch) { p->at++; return 1; }
    return 0;
}
static int text_byte(parser *p, unsigned char ch) {
    cc_core *c=p->c;
    if (c->text_used>=c->workspace.text_capacity) return fail(c,"text-storage");
    c->workspace.text[c->text_used++]=ch; return 1;
}
static int hex4(parser *p, uint32_t *x) {
    size_t i; unsigned char ch; uint32_t n=0;
    for (i=0;i<4;i++) {
        if (p->at>=p->c->frame_size) return 0;
        ch=p->c->workspace.frame[p->at++]; n<<=4;
        if (ch>='0'&&ch<='9') n|=ch-'0';
        else if (ch>='a'&&ch<='f') n|=ch-'a'+10;
        else if (ch>='A'&&ch<='F') n|=ch-'A'+10;
        else return 0;
    }
    *x=n; return 1;
}
static int encoded_scalar(parser *p, uint32_t x) {
    if (x<0x80) return text_byte(p,(unsigned char)x);
    if (x<0x800) return text_byte(p,(unsigned char)(0xc0|(x>>6))) && text_byte(p,(unsigned char)(0x80|(x&63)));
    if (x<0x10000) return text_byte(p,(unsigned char)(0xe0|(x>>12))) && text_byte(p,(unsigned char)(0x80|((x>>6)&63))) && text_byte(p,(unsigned char)(0x80|(x&63)));
    return text_byte(p,(unsigned char)(0xf0|(x>>18))) && text_byte(p,(unsigned char)(0x80|((x>>12)&63))) && text_byte(p,(unsigned char)(0x80|((x>>6)&63))) && text_byte(p,(unsigned char)(0x80|(x&63)));
}
static int string(parser *p, cc_view *v) {
    cc_core *c=p->c; size_t begin=c->text_used,k,i; uint32_t x,y; unsigned char ch;
    if (!take(p,'"')) return 0;
    while (p->at<c->frame_size) {
        ch=c->workspace.frame[p->at++];
        if (ch=='"') { *v=view(c->workspace.text+begin,c->text_used-begin); return 1; }
        if (ch<0x20) return 0;
        if (ch=='\\') {
            if (p->at>=c->frame_size) return 0;
            ch=c->workspace.frame[p->at++];
            switch (ch) {
            case '"': case '\\': case '/': if (!text_byte(p,ch)) return 0; break;
            case 'b': if (!text_byte(p,8)) return 0; break;
            case 'f': if (!text_byte(p,12)) return 0; break;
            case 'n': if (!text_byte(p,10)) return 0; break;
            case 'r': if (!text_byte(p,13)) return 0; break;
            case 't': if (!text_byte(p,9)) return 0; break;
            case 'u':
                if (!hex4(p,&x)) return 0;
                if (x>=0xd800 && x<=0xdbff) {
                    if (!take(p,'\\') || !take(p,'u') || !hex4(p,&y) || y<0xdc00 || y>0xdfff) return 0;
                    x=0x10000+((x-0xd800)<<10)+(y-0xdc00);
                } else if (x>=0xdc00 && x<=0xdfff) return 0;
                if (!encoded_scalar(p,x)) return 0;
                break;
            default: return 0;
            }
        } else {
            p->at--;
            if (!scalar(c->workspace.frame+p->at,c->frame_size-p->at,&k,&x)) return 0;
            for (i=0;i<k;i++) if (!text_byte(p,c->workspace.frame[p->at+i])) return 0;
            p->at+=k;
        }
    }
    return 0;
}
static size_t value(parser *p, unsigned depth, cc_view key) {
    cc_core *c=p->c; cc_node *n; size_t index,child,last=CC_NONE,other; cc_view name;
    unsigned char ch; int negative=0; uint64_t x=0,limit; size_t digits;
    if (depth>64 || c->node_count>=c->workspace.node_capacity) { fail(c,"node-storage-or-depth"); return CC_NONE; }
    ws(p); if (p->at>=c->frame_size) return CC_NONE;
    index=c->node_count++; n=c->workspace.nodes+index; memset(n,0,sizeof *n); n->key=key; n->child=n->next=CC_NONE;
    ch=c->workspace.frame[p->at];
    if (ch=='{' || ch=='[') {
        int object=ch=='{'; unsigned char close=object?'}':']';
        if (depth>=64) { fail(c,"json-depth"); return CC_NONE; }
        n->type=object?CC_OBJECT:CC_ARRAY; p->at++; ws(p);
        if (take(p,close)) return index;
        for (;;) {
            name=view(NULL,0);
            if (object) {
                if (!string(p,&name)) return CC_NONE;
                for (other=n->child;other!=CC_NONE;other=c->workspace.nodes[other].next)
                    if (equal(name,c->workspace.nodes[other].key)) { fail(c,"duplicate-decoded-key"); return CC_NONE; }
                ws(p); if (!take(p,':')) return CC_NONE;
            }
            child=value(p,depth+1,name); if (child==CC_NONE) return CC_NONE;
            if (last==CC_NONE) n->child=child; else c->workspace.nodes[last].next=child;
            last=child; ws(p); if (take(p,close)) return index;
            if (!take(p,',')) return CC_NONE;
            ws(p);
        }
    }
    if (ch=='"') { n->type=CC_STRING; return string(p,&n->text)?index:CC_NONE; }
    if (ch=='t' || ch=='f') {
        const char *s=ch=='t'?"true":"false"; size_t len=strlen(s);
        if (c->frame_size-p->at<len || memcmp(c->workspace.frame+p->at,s,len)) return CC_NONE;
        p->at+=len; n->type=CC_BOOLEAN; n->boolean=ch=='t'; return index;
    }
    if (take(p,'-')) negative=1;
    digits=p->at; limit=negative?(uint64_t)INT64_MAX+1u:(uint64_t)INT64_MAX;
    while (p->at<c->frame_size && c->workspace.frame[p->at]>='0' && c->workspace.frame[p->at]<='9') {
        unsigned d=c->workspace.frame[p->at++]-'0';
        if (x>(limit-d)/10u) return CC_NONE;
        x=x*10u+d;
    }
    if (p->at==digits || (p->at-digits>1 && c->workspace.frame[digits]=='0')) return CC_NONE;
    n->type=CC_INTEGER;
    n->integer=negative?(x==(uint64_t)INT64_MAX+1u?INT64_MIN:-(int64_t)x):(int64_t)x;
    return index;
}
const cc_node *cc_get_node(const cc_core *c,size_t i) { return i<c->node_count?c->workspace.nodes+i:NULL; }
size_t cc_field(const cc_core *c,size_t object,const char *key) {
    size_t i; const cc_node *n=cc_get_node(c,object);
    if (!n || n->type!=CC_OBJECT) return CC_NONE;
    for (i=n->child;i!=CC_NONE;i=c->workspace.nodes[i].next) if (literal(c->workspace.nodes[i].key,key)) return i;
    return CC_NONE;
}
static int exact(const cc_core *c,size_t object,const char *const *keys,size_t count) {
    size_t i,k,n=0; const cc_node *o=cc_get_node(c,object);
    if (!o || o->type!=CC_OBJECT) return 0;
    for (i=o->child;i!=CC_NONE;i=c->workspace.nodes[i].next) n++;
    if (n!=count) return 0;
    for (k=0;k<count;k++) if (cc_field(c,object,keys[k])==CC_NONE) return 0;
    return 1;
}
static int str_field(const cc_core *c,size_t o,const char *key,cc_view *out) {
    const cc_node *n=cc_get_node(c,cc_field(c,o,key));
    if (!n || n->type!=CC_STRING) return 0;
    *out=n->text; return 1;
}
static int int_field(const cc_core *c,size_t o,const char *key,int64_t *out) {
    const cc_node *n=cc_get_node(c,cc_field(c,o,key));
    if (!n || n->type!=CC_INTEGER) return 0;
    *out=n->integer; return 1;
}
static size_t lease_index(const cc_core *c,cc_view lease) {
    size_t i;
    if (!digest(lease)) return CC_NONE;
    for (i=0;i<c->lease_count;i++) if (equal(lease,view(c->leases[i].identity,64))) return i;
    return CC_NONE;
}
static int request(cc_core *c) {
    static const char *const envelope[]={"schema","identity","ordinal","action","payload"};
    static const char *const binding[]={"input","admissionSha256"};
    static const char *const retention[]={"bytes"};
    static const char *const command[]={"Executable","Arguments","Environment","WorkingDirectory","OutputLimit"};
    static const char *const observation[]={"lease"};
    parser p; size_t root,payload,i,arguments,environment,l; cc_view schema,identity,action,s,a; int64_t ordinal,num;
    c->node_count=c->text_used=0; p.c=c; p.at=0; root=value(&p,0,view(NULL,0)); ws(&p);
    if (root==CC_NONE || p.at!=c->frame_size || !exact(c,root,envelope,5) ||
        !str_field(c,root,"schema",&schema) || !literal(schema,CC_SCHEMA) ||
        !str_field(c,root,"identity",&identity) || !equal(identity,view(c->identity,64)) ||
        !int_field(c,root,"ordinal",&ordinal) || c->ordinal==INT32_MAX || ordinal!=(int64_t)c->ordinal+1 ||
        !str_field(c,root,"action",&action)) return fail(c,"request-envelope");
    memset(&c->request,0,sizeof c->request); c->request.command=CC_NONE; c->request.ordinal=(int32_t)ordinal;
    payload=cc_field(c,root,"payload");
    if (literal(action,"bind-input")) {
        if (c->bound || !exact(c,payload,binding,2) || !str_field(c,payload,"input",&s) || !equal(s,view(c->input,64)) ||
            !str_field(c,payload,"admissionSha256",&a) || !equal(a,view(c->admission,64))) return fail(c,"input-binding");
        c->request.action=CC_BIND_INPUT; c->request.input=s;
    } else if (literal(action,"retain")) {
        if (!exact(c,payload,retention,1) || !int_field(c,payload,"bytes",&num) || num<0 || num>INT32_MAX ||
            (uint64_t)num>c->allowance-c->charged) return fail(c,"retention-charge");
        c->request.action=CC_RETAIN; c->request.retained_bytes=(uint64_t)num;
    } else if (literal(action,"start")) {
        if (!c->bound || !exact(c,payload,command,5) || !str_field(c,payload,"Executable",&s) ||
            !literal(s,"/usr/sbin/podman") || !str_field(c,payload,"WorkingDirectory",&s) ||
            !int_field(c,payload,"OutputLimit",&num) || num!=CC_OUTPUT_MAX) return fail(c,"command-shape-selection");
        arguments=cc_field(c,payload,"Arguments"); environment=cc_field(c,payload,"Environment");
        if (c->workspace.nodes[arguments].type!=CC_ARRAY || c->workspace.nodes[environment].type!=CC_OBJECT) return fail(c,"command-collections");
        for (i=c->workspace.nodes[arguments].child;i!=CC_NONE;i=c->workspace.nodes[i].next)
            if (c->workspace.nodes[i].type!=CC_STRING) return fail(c,"command-argument-type");
        for (i=c->workspace.nodes[environment].child;i!=CC_NONE;i=c->workspace.nodes[i].next)
            if (c->workspace.nodes[i].type!=CC_STRING) return fail(c,"command-environment-type");
        if (c->lease_count==c->lease_capacity) return fail(c,"lease-storage");
        c->request.action=CC_START; c->request.command=payload;
    } else if (literal(action,"observe") || literal(action,"cancel")) {
        if (!c->bound || !exact(c,payload,observation,1) || !str_field(c,payload,"lease",&s) ||
            (l=lease_index(c,s))==CC_NONE || c->leases[l].retired) return fail(c,"lease-join");
        c->request.action=literal(action,"observe")?CC_OBSERVE:CC_CANCEL; c->request.lease=s;
    } else return fail(c,"unknown-action");
    c->pending=1; return 1;
}
int cc_init(cc_core *c,cc_workspace w,cc_lease *leases,size_t count,cc_view identity,cc_view admission,cc_view input,uint64_t now,uint64_t deadline,uint64_t allowance) {
    memset(c,0,sizeof *c); c->workspace=w; c->leases=leases; c->lease_capacity=count;
    c->deadline_ms=deadline; c->allowance=allowance;
    if (!digest(identity) || !digest(admission) || !digest(input) || deadline<=now || deadline-now>5400000u ||
        !w.frame || w.frame_capacity<CC_FRAME_MAX || !w.text || w.text_capacity<CC_FRAME_MAX ||
        !w.nodes || !w.node_capacity || !leases || !count) return fail(c,"core-binding-or-storage");
    memcpy(c->identity,identity.data,64); memcpy(c->admission,admission.data,64); memcpy(c->input,input.data,64); return 1;
}
cc_progress cc_feed(cc_core *c,const unsigned char *bytes,size_t count,uint64_t now) {
    size_t n;
    if (!alive(c,now)) return CC_FAILED;
    if (c->pending || (count && !bytes)) { fail(c,"pending-or-input"); return CC_FAILED; }
    while (c->header_used<4 && count) { c->header[c->header_used++]=*bytes++; count--; }
    if (c->header_used<4) return CC_MORE;
    if (!c->frame_size) {
        c->frame_size=((size_t)c->header[0]<<24)|((size_t)c->header[1]<<16)|((size_t)c->header[2]<<8)|c->header[3];
        if (!c->frame_size || c->frame_size>CC_FRAME_MAX) { fail(c,"frame-length"); return CC_FAILED; }
    }
    n=c->frame_size-c->frame_used;
    if (count>n) { fail(c,"frame-surplus"); return CC_FAILED; }
    if (count) memcpy(c->workspace.frame+c->frame_used,bytes,count);
    c->frame_used+=count;
    if (c->frame_used<c->frame_size) return CC_MORE;
    return request(c)?CC_READY:CC_FAILED;
}
int cc_finish_input(cc_core *c) {
    if (c->poisoned) return 0;
    return fail(c,"transport-ended"); /* EOF never certifies a reusable channel. */
}
const cc_request *cc_current(const cc_core *c) { return c->pending && !c->poisoned?&c->request:NULL; }
/* Bounded encoder: JSON escaping can exceed the frame even when decoded output
 * fits; all writes are checked before committing protocol or lease state. */
typedef struct { unsigned char *p; size_t used,capacity; int good; } writer;
static void put(writer *w,const void *p,size_t n) {
    if (!w->good || n>w->capacity-w->used || n>CC_FRAME_MAX-w->used) { w->good=0; return; }
    if (n) memcpy(w->p+w->used,p,n);
    w->used+=n;
}
static void word(writer *w,const char *s) { put(w,s,strlen(s)); }
static void quote(writer *w,cc_view s) {
    size_t i; unsigned char ch; static const char h[]="0123456789abcdef";
    word(w,"\"");
    for (i=0;i<s.size && w->good;i++) {
        ch=s.data[i];
        if (ch=='"' || ch=='\\') { unsigned char e[2]={'\\',ch}; put(w,e,2); }
        else if (ch<0x20) { unsigned char e[6]={'\\','u','0','0',(unsigned char)h[ch>>4],(unsigned char)h[ch&15]}; put(w,e,6); }
        else put(w,&ch,1);
    }
    word(w,"\"");
}
static void number(writer *w,int64_t number) {
    unsigned char digits[21]; size_t n=0; uint64_t x;
    if (number<0) { word(w,"-"); x=(uint64_t)(-(number+1))+1u; } else x=(uint64_t)number;
    do { digits[n++]=(unsigned char)('0'+x%10u); x/=10u; } while (x);
    while (n) put(w,digits+--n,1);
}
int cc_complete(cc_core *c,const cc_result *r,uint64_t now,unsigned char *reply,size_t capacity,size_t *written) {
    writer w; size_t l=CC_NONE; uint64_t output=0,delta=0;
    if (written) *written=0;
    if (!alive(c,now)) return 0;
    if (!c->pending || !r || r->accepted!=1 || !reply || capacity<4 || !written) return fail(c,"adapter-result-or-reply-storage");
    if (c->request.action==CC_START) {
        if (!digest(r->lease) || lease_index(c,r->lease)!=CC_NONE) return fail(c,"start-result-lease");
    } else if (c->request.action==CC_OBSERVE || c->request.action==CC_CANCEL) {
        l=lease_index(c,c->request.lease);
        if (l==CC_NONE || !equal(r->lease,c->request.lease) || (r->retired!=0 && r->retired!=1) ||
            r->stdout_bytes.size>CC_OUTPUT_MAX || r->stderr_bytes.size>CC_OUTPUT_MAX-r->stdout_bytes.size ||
            !utf8(r->stdout_bytes) || !utf8(r->stderr_bytes)) return fail(c,"observation-result");
        output=(uint64_t)r->stdout_bytes.size+(uint64_t)r->stderr_bytes.size;
        if (output<c->leases[l].observed_bytes) return fail(c,"output-regression");
        delta=output-c->leases[l].observed_bytes;
        if (delta>c->allowance-c->charged) return fail(c,"aggregate-output-charge");
    }
    w.p=reply+4; w.used=0; w.capacity=capacity-4; w.good=1;
    word(&w,"{\"schema\":\"" CC_SCHEMA "\",\"identity\":"); quote(&w,view(c->identity,64));
    word(&w,",\"ordinal\":"); number(&w,c->request.ordinal); word(&w,",\"status\":\"acknowledged\",\"payload\":{");
    switch (c->request.action) {
    case CC_BIND_INPUT: word(&w,"\"input\":"); quote(&w,c->request.input); break;
    case CC_RETAIN: word(&w,"\"charged\":"); number(&w,(int64_t)c->request.retained_bytes); break;
    case CC_START: word(&w,"\"lease\":"); quote(&w,r->lease); break;
    case CC_OBSERVE: case CC_CANCEL:
        word(&w,"\"lease\":"); quote(&w,r->lease); word(&w,",\"retired\":"); word(&w,r->retired?"true":"false");
        word(&w,",\"exitCode\":"); number(&w,r->exit_code); word(&w,",\"stdout\":"); quote(&w,r->stdout_bytes);
        word(&w,",\"stderr\":"); quote(&w,r->stderr_bytes); break;
    }
    word(&w,"}}");
    if (!w.good) return fail(c,"reply-frame-bound");
    reply[0]=(unsigned char)(w.used>>24); reply[1]=(unsigned char)(w.used>>16); reply[2]=(unsigned char)(w.used>>8); reply[3]=(unsigned char)w.used;
    switch (c->request.action) {
    case CC_BIND_INPUT: memcpy(c->input,c->request.input.data,64); c->bound=1; break;
    case CC_RETAIN: c->charged+=c->request.retained_bytes; break;
    case CC_START:
        memcpy(c->leases[c->lease_count].identity,r->lease.data,64);
        c->leases[c->lease_count].observed_bytes=0; c->leases[c->lease_count].retired=0; c->lease_count++; break;
    case CC_OBSERVE: case CC_CANCEL:
        c->charged+=delta; c->leases[l].observed_bytes=output; c->leases[l].retired=r->retired; break;
    }
    c->ordinal=c->request.ordinal; c->pending=0; c->header_used=c->frame_used=c->frame_size=0;
    *written=w.used+4; return 1;
}
int cc_cached_retirement(cc_core *c,cc_view lease,uint64_t now) {
    size_t l;
    if (!alive(c,now)) return 0;
    l=lease_index(c,lease);
    if (!c->bound || l==CC_NONE) return fail(c,"cached-lease-join");
    return c->leases[l].retired;
}
