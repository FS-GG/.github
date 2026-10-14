#define _GNU_SOURCE
#include "custody_root.h"
#include <stdio.h>
#include <string.h>
#include <limits.h>
#include <stdlib.h>

static int lr_fail(lr_state *s,const char *why) {
 if(!s->failure)s->failure=why;
 s->poisoned=1;
 return 0;
}
void lr_poison(lr_state *s,const char *why) { if(s)lr_fail(s,why); }
static int lr_same(cc_view x,const char *text) {
 size_t n=strlen(text);return x.data&&x.size==n&&!memcmp(x.data,text,n);
}
static int lr_hex(cc_view x,size_t count) {
 if(!x.data||x.size!=count)return 0;
 for(size_t i=0;i<count;i++)if(!((x.data[i]>='0'&&x.data[i]<='9')||(x.data[i]>='a'&&x.data[i]<='f')))return 0;
 return 1;
}
static int lr_text(cc_view x) {
 size_t i=0;
 if(x.size>=LC_STRING_MAX||(x.size&&!x.data))return 0;
 while(i<x.size) {
  unsigned char b=x.data[i++]; unsigned n; uint32_t cp,min;
  if(!b)return 0;
  if(b<128)continue;
  if(b>=0xc2&&b<=0xdf){n=1;cp=b&31u;min=128;}
  else if(b>=0xe0&&b<=0xef){n=2;cp=b&15u;min=2048;}
  else if(b>=0xf0&&b<=0xf4){n=3;cp=b&7u;min=65536;}
  else return 0;
  if(n>x.size-i)return 0;
  while(n--){b=x.data[i++];if((b&0xc0u)!=0x80u)return 0;cp=(cp<<6)|(b&63u);}
  if(cp<min||cp>0x10ffffu||(cp>=0xd800u&&cp<=0xdfffu))return 0;
 }
 return 1;
}
static int lr_path(cc_view x) {
 if(!lr_text(x)||x.size<2||x.data[0]!='/')return 0;
 size_t component=1;
 for(size_t i=1;i<=x.size;i++) {
  if(i==x.size||x.data[i]=='/') {
   size_t n=i-component;
   if(!n||(n==1&&x.data[component]=='.')||(n==2&&x.data[component]=='.'&&x.data[component+1]=='.'))return 0;
   component=i+1;
  } else {unsigned char c=x.data[i];if(!((c>='A'&&c<='Z')||(c>='a'&&c<='z')||(c>='0'&&c<='9')||c=='.'||c=='_'||c=='-'))return 0;}
 }
 return 1;
}
static int lr_command_shape(const lr_command *c) {
 if(!c||!lr_same(c->executable,"/usr/sbin/podman")||!lr_path(c->working_directory)||
  c->output_limit!=CC_OUTPUT_MAX||c->argument_count>LC_VECTOR_MAX||c->environment_count>LC_VECTOR_MAX||
  (c->argument_count&&!c->arguments)||(c->environment_count&&!c->environment))return 0;
 for(size_t i=0;i<c->argument_count;i++)if(!lr_text(c->arguments[i]))return 0;
 for(size_t i=0;i<c->environment_count;i++) {
  const lc_environment *e=&c->environment[i];
  if(!lr_text(e->name)||!e->name.size||memchr(e->name.data,'=',e->name.size)||!lr_text(e->value))return 0;
  for(size_t j=0;j<i;j++)if(e->name.size==c->environment[j].name.size&&!memcmp(e->name.data,c->environment[j].name.data,e->name.size))return 0;
 }
 return 1;
}
static int lr_expected_step(size_t index,size_t normal,char *out,size_t capacity) {
 static const char *build[]={"parent-load","parent-inspect","build","build-save"};
 static const char *reload[]={"reload-load","reload-inspect","reload-save","probe-dotnet","probe-python","probe-native"};
 static const char *inactive[]={"inactive-inspect","inactive-manager","inactive-host"};
 static const char *cleanup[]={"census","empty","mount"};
 const char *name=NULL;unsigned bundle=0;int store=-1;
 if(index<8){bundle=(unsigned)(index/4);name=build[index%4];}
 else if(index<20){index-=8;bundle=(unsigned)(index/6);name=reload[index%6];}
 else if(index<normal){index-=20;bundle=(unsigned)(index/3);name=inactive[index%3];}
 else {index-=normal;store=(int)(index/3);bundle=(unsigned)store/2;name=cleanup[index%3];}
 int n=store<0?snprintf(out,capacity,"%c/%s",'a'+bundle,name):snprintf(out,capacity,"%c/cleanup-%s-%s",'a'+bundle,store%2?"reload":"build",name);
 return n>0&&(size_t)n<capacity;
}
int lr_validate(const lr_recipe *r,const char **failure) {
 const char *why="root-recipe-layout";size_t total=0;
 if(!r||r->version!=LR_VERSION||r->size!=sizeof *r)goto bad;
 why="root-recipe-identity";
 if(!lr_same(r->schema,LR_SCHEMA)||!lr_hex(r->recipe_sha256,64)||!lr_hex(r->expected_input_sha256,64)||!lr_hex(r->custody_identity,64)||
  !lr_hex(r->bundle_nonce[0],32)||!lr_hex(r->bundle_nonce[1],32)||!memcmp(r->bundle_nonce[0].data,r->bundle_nonce[1].data,32))goto bad;
 why="root-recipe-bounds";
 size_t normal=r->target==LR_C4?20:r->target==LR_INACTIVE?26:0;
 if(!normal||r->normal_count!=normal||r->step_count!=normal+12||!r->steps||r->removal_limit>LR_REMOVAL_MAX||
  r->maximum_commands!=r->step_count+r->removal_limit||r->maximum_commands>LC_SLOT_MAX||
  !lr_path(r->work_root)||!lr_path(r->evidence_root))goto bad;
 for(size_t i=0;i<r->step_count+LR_STORE_COUNT;i++) {
  const lr_command *c=i<r->step_count?&r->steps[i].command:&r->removal_prefix[i-r->step_count];
  why="root-recipe-command";if(!lr_command_shape(c))goto bad;
  if(i<r->step_count){char step[64];why="root-recipe-step";if(!lr_expected_step(i,normal,step,sizeof step)||!lr_same(r->steps[i].step,step))goto bad;}
  size_t bytes=c->executable.size+c->working_directory.size+2;
  if(bytes>LC_STRING_MAX)goto bad;
  for(size_t j=0;j<c->argument_count;j++){if(bytes>LC_STRING_MAX-c->arguments[j].size-1)goto bad;bytes+=c->arguments[j].size+1;}
  for(size_t j=0;j<c->environment_count;j++){size_t n=c->environment[j].name.size,v=c->environment[j].value.size;if(n>LC_STRING_MAX-v-2||bytes>LC_STRING_MAX-n-v-2)goto bad;bytes+=n+v+2;}
  why="root-recipe-retained-bound";if(total>LC_STRING_MAX-bytes)goto bad;total+=bytes;
 }
 if(failure)*failure=NULL;
 return 1;
 bad:if(failure)*failure=why;return 0;
}
int lr_init(lr_state *s,const lr_recipe *r,const char **failure) {
 if(!s||!lr_validate(r,failure))return 0;
 memset(s,0,sizeof *s);s->recipe=r;return 1;
}
int lr_note_bundle(lr_state *s,unsigned bundle) {
 if(!s||s->poisoned||bundle>1||s->terminal)return 0;
 if(s->cleanup_started&&s->store/2>bundle)return lr_fail(s,"root-cleaned-bundle-reappeared");
 s->known_bundles|=1u<<bundle;return 1;
}
static void lr_skip_absent(lr_state *s) {
 while(s->store<LR_STORE_COUNT&&!(s->known_bundles&(1u<<(s->store/2))))s->store++;
 if(s->store==LR_STORE_COUNT)s->terminal=1;
}
int lr_select(lr_state *s,int cleanup,lr_selection *selection) {
 if(!s||!selection||s->poisoned||s->pending||s->terminal)return 0;
 memset(selection,0,sizeof *selection);
 if(!s->cleanup_started&&(cleanup||s->normal_cursor==s->recipe->normal_count)){
  s->cleanup_started=1;s->aborting|=s->normal_cursor!=s->recipe->normal_count;lr_skip_absent(s);
 }
 if(!s->cleanup_started) {
  if(cleanup||s->aborting||s->normal_cursor>=s->recipe->normal_count)return lr_fail(s,"root-normal-step-unavailable");
  selection->command=&s->recipe->steps[s->normal_cursor].command;s->pending_kind=0;
  selection->bundle=s->normal_cursor<8?s->normal_cursor/4:s->normal_cursor<20?(s->normal_cursor-8)/6:(s->normal_cursor-20)/3;
 } else {
  if(s->terminal)return lr_fail(s,"root-cleanup-terminal-start");
  if(!cleanup)return lr_fail(s,"root-normal-restart-after-cleanup");
  selection->bundle=s->store/2;s->pending_store=s->store;
  if(s->phase==1){selection->command=&s->recipe->removal_prefix[s->store];selection->removal_id=(cc_view){s->census[s->removed],64};s->pending_kind=2;}
  else {unsigned phase=s->phase==0?0:s->phase==2?1:2;selection->command=&s->recipe->steps[s->recipe->normal_count+s->store*3+phase].command;s->pending_kind=1+phase*2;}
 }
 s->pending=1;return 1;
}
int lr_retired(lr_state *s,int exit_code,cc_view output,int stdout_eof,int stderr_eof) {
 if(!s||s->poisoned||!s->pending||!stdout_eof||!stderr_eof)return s?lr_fail(s,"root-result-retirement-unknown"):0;
 s->pending=0;
 if(s->pending_kind==0){s->normal_cursor++;if(exit_code)s->aborting=1;return 1;}
 if(exit_code)return lr_fail(s,"root-cleanup-command-failed");
 if(s->pending_kind==1) {
  s->census_count=s->removed=0;
  if(output.size&&!output.data)return lr_fail(s,"root-census-missing-output");
  if(output.size%65||output.size/65>s->recipe->removal_limit-s->total_removed)return lr_fail(s,"root-census-bound-or-framing");
  for(size_t at=0;at<output.size;at+=65){cc_view id={output.data+at,64};if(output.data[at+64]!='\n'||!lr_hex(id,64))return lr_fail(s,"root-census-identity");
   for(unsigned j=0;j<s->census_count;j++)if(!memcmp(id.data,s->census[j],64))return lr_fail(s,"root-census-duplicate");
   memcpy(s->census[s->census_count++],id.data,64);
  }
  s->phase=s->census_count?1:2;
 } else if(s->pending_kind==2) {
  if(s->removed>=s->census_count||s->total_removed>=s->recipe->removal_limit)return lr_fail(s,"root-removal-budget");
  s->removed++;s->total_removed++;if(s->removed==s->census_count)s->phase=2;
 } else if(s->pending_kind==3) {
  if(output.size)return lr_fail(s,"root-fresh-census-not-empty");
  s->phase=3;
 } else {
  if(output.size)return lr_fail(s,"root-mounts-not-empty");
  s->store++;s->phase=0;lr_skip_absent(s);
 }
 return 1;
}
int lr_terminal(const lr_state *s) { return s&&!s->poisoned&&!s->pending&&s->cleanup_started&&s->terminal; }
static int lr_decimal(const char **at,uint32_t *value) {
 uint32_t n=0;const char *p=*at;
 if(*p<'0'||*p>'9')return 0;
 do {unsigned d=(unsigned)(*p-'0');if(n>(UINT32_MAX-d)/10u)return 0;n=n*10u+d;p++;}while(*p>='0'&&*p<='9');
 *at=p;*value=n;return 1;
}
static int lr_space(const char **at) {
 const char *p=*at;if(*p!=' '&&*p!='\t')return 0;
 while(*p==' '||*p=='\t')p++;
 *at=p;return 1;
}
int lr_mapped_uid0(const char *text,uint32_t *uid) {
 uint32_t in[2],out[2],count[2];const char *at=text;
 if(!text||!uid)return 0;
 for(unsigned i=0;i<2;i++) {
  while(*at==' '||*at=='\t')at++;
  if(!lr_decimal(&at,&in[i])||!lr_space(&at)||!lr_decimal(&at,&out[i])||!lr_space(&at)||!lr_decimal(&at,&count[i]))return 0;
  while(*at==' '||*at=='\t')at++;
  if(*at!='\n')return 0;
  at++;
 }
 if(*at||in[0]!=0||in[1]!=32768||count[0]!=1||count[1]!=1||out[0]==0||out[1]==0||out[0]==UINT32_MAX||out[1]==UINT32_MAX||out[0]==out[1])return 0;
 *uid=out[0];return 1;
}

struct lr_recipe_copy {
 lr_recipe recipe; lr_step steps[LR_RECIPE_MAX];
 cc_view vectors[LC_VECTOR_MAX]; lc_environment environment[LC_VECTOR_MAX];
 size_t bytes,vectors_used,environment_used; unsigned char text[LC_STRING_MAX];
};
static cc_view lr_copy_text(lr_recipe_copy *c,cc_view x) {
 if(x.size>=LC_STRING_MAX-c->bytes)return (cc_view){0};
 cc_view r={c->text+c->bytes,x.size};memcpy(c->text+c->bytes,x.data,x.size);c->text[c->bytes+x.size]=0;c->bytes+=x.size+1;return r;
}
static int lr_copy_command(lr_recipe_copy *c,lr_command *to,const lr_command *from) {
 *to=*from;to->executable=lr_copy_text(c,from->executable);to->working_directory=lr_copy_text(c,from->working_directory);
 if(!to->executable.data||!to->working_directory.data||from->argument_count>LC_VECTOR_MAX-c->vectors_used||from->environment_count>LC_VECTOR_MAX-c->environment_used)return 0;
 to->arguments=c->vectors+c->vectors_used;to->environment=c->environment+c->environment_used;
 for(size_t i=0;i<from->argument_count;i++){cc_view x=lr_copy_text(c,from->arguments[i]);if(!x.data)return 0;c->vectors[c->vectors_used++]=x;}
 for(size_t i=0;i<from->environment_count;i++){lc_environment e={lr_copy_text(c,from->environment[i].name),lr_copy_text(c,from->environment[i].value)};if(!e.name.data||!e.value.data)return 0;c->environment[c->environment_used++]=e;}
 return 1;
}
lr_recipe_copy *lr_copy_recipe(const lr_recipe *r) {
 if(!lr_validate(r,NULL))return NULL;
 lr_recipe_copy *c=calloc(1,sizeof *c);if(!c)return NULL;
 c->recipe=*r;c->recipe.steps=c->steps;
 c->recipe.schema=lr_copy_text(c,r->schema);c->recipe.recipe_sha256=lr_copy_text(c,r->recipe_sha256);
 c->recipe.expected_input_sha256=lr_copy_text(c,r->expected_input_sha256);c->recipe.custody_identity=lr_copy_text(c,r->custody_identity);
 c->recipe.work_root=lr_copy_text(c,r->work_root);c->recipe.evidence_root=lr_copy_text(c,r->evidence_root);
 for(unsigned i=0;i<2;i++)c->recipe.bundle_nonce[i]=lr_copy_text(c,r->bundle_nonce[i]);
 if(!c->recipe.bundle_nonce[1].data)goto bad;
 for(size_t i=0;i<r->step_count;i++){c->steps[i].step=lr_copy_text(c,r->steps[i].step);if(!c->steps[i].step.data||!lr_copy_command(c,&c->steps[i].command,&r->steps[i].command))goto bad;}
 for(unsigned i=0;i<LR_STORE_COUNT;i++)if(!lr_copy_command(c,&c->recipe.removal_prefix[i],&r->removal_prefix[i]))goto bad;
 return c;
 bad:free(c);return NULL;
}
const lr_recipe *lr_copied_recipe(const lr_recipe_copy *c){return c?&c->recipe:NULL;}
void lr_free_recipe(lr_recipe_copy *c){free(c);}
