#ifndef FSGG_LEARN_CUSTODY_CORE_H
#define FSGG_LEARN_CUSTODY_CORE_H

#include <stddef.h>
#include <stdint.h>

/* Internal, allocation-free protocol/state core. No effect or admission authority.
 * All storage belongs to the caller. A core and its workspace are single-owner.
 * Times are caller-observed milliseconds on one immutable deadline clock.
 * Views in a decoded request live until the next feed; result views need only
 * remain alive during complete(). The caller owns transport and authentic results. */
#define CC_FRAME_MAX (2u * 1024u * 1024u)
#define CC_OUTPUT_MAX (1024u * 1024u)
#define CC_SCHEMA "fsgg.learn.command-custody/1"
#define CC_NONE SIZE_MAX

typedef struct { const unsigned char *data; size_t size; } cc_view;
typedef enum { CC_OBJECT, CC_ARRAY, CC_STRING, CC_INTEGER, CC_BOOLEAN } cc_type;
typedef struct {
    cc_type type;
    cc_view key, text;
    int64_t integer;
    int boolean;
    size_t child, next;
} cc_node;
typedef struct {
    unsigned char *frame, *text;
    size_t frame_capacity, text_capacity;
    cc_node *nodes;
    size_t node_capacity;
} cc_workspace;
typedef enum { CC_BIND_INPUT, CC_RETAIN, CC_START, CC_OBSERVE, CC_CANCEL } cc_action;
typedef struct {
    cc_action action;
    int32_t ordinal;
    cc_view input, lease;
    uint64_t retained_bytes;
    /* START references actual parsed command nodes. No executable authorization. */
    size_t command;
} cc_request;
typedef struct {
    unsigned char identity[64];
    uint64_t observed_bytes;
    int retired;
} cc_lease;
typedef struct {
    cc_workspace workspace;
    cc_lease *leases;
    size_t lease_capacity, lease_count;
    unsigned char identity[64], admission[64], input[64];
    uint64_t deadline_ms, allowance, charged;
    int32_t ordinal;
    int bound, poisoned, pending;
    size_t header_used, frame_used, frame_size, node_count, text_used;
    unsigned char header[4];
    cc_request request;
    const char *failure;
} cc_core;
typedef struct {
    /* accepted means an explicit adapter result, never receipt of a request. */
    int accepted;
    cc_view lease;
    int retired;
    int32_t exit_code;
    cc_view stdout_bytes, stderr_bytes;
} cc_result;
typedef enum { CC_FAILED = -1, CC_MORE = 0, CC_READY = 1 } cc_progress;

int cc_init(cc_core *, cc_workspace, cc_lease *, size_t lease_capacity,
            cc_view identity, cc_view admission, cc_view expected_input, uint64_t now_ms,
            uint64_t original_deadline_ms, uint64_t aggregate_allowance);
/* Feed exactly one framed request, allowing fragmentation. Surplus data refuses;
 * call complete before another frame. finish_input refuses incomplete framing. */
cc_progress cc_feed(cc_core *, const unsigned char *, size_t, uint64_t now_ms);
int cc_finish_input(cc_core *);
const cc_request *cc_current(const cc_core *);
const cc_node *cc_get_node(const cc_core *, size_t);
size_t cc_field(const cc_core *, size_t object, const char *key);
/* Writes an exact length-prefixed F# acknowledgment. No state commits on failure.
 * Caller must poison on any write loss/partial delivery; reconnect is forbidden. */
int cc_complete(cc_core *, const cc_result *, uint64_t now_ms,
                unsigned char *reply, size_t capacity, size_t *written);
int cc_cached_retirement(cc_core *, cc_view lease, uint64_t now_ms);
void cc_poison(cc_core *, const char *reason);

#endif
