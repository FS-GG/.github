/* LEARN #4745 socket probe (spec rev 10 §4 / A2; option (a), fdev 4516#6347898882).
 * usage: probe <expected-errno-for-AF_VSOCK>
 * AF_UNIX and AF_INET stream sockets must be creatable (no network is used: nothing is connected);
 * socket(AF_VSOCK=40, SOCK_STREAM, 0) must FAIL with exactly the expected errno (the effective seccomp rule's errnoRet).
 * Prints exactly one line, "PROBE ok" or "PROBE fail <detail>", to stdout; exit 0 on ok, 1 on fail, 2 on usage.
 * Built statically on the host (learn-build-probe.sh) and copied into the reader image r4 as /usr/local/libexec/learn/probe. */
#include <errno.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/socket.h>
#include <unistd.h>

#ifndef AF_VSOCK
#define AF_VSOCK 40
#endif

static int try_socket(int family, int *err) {
    int fd = socket(family, SOCK_STREAM, 0);
    if (fd < 0) { *err = errno; return 0; }
    close(fd); *err = 0; return 1;
}

int main(int argc, char **argv) {
    char *end = NULL; long want;
    int e_unix, e_inet, e_vsock, ok_unix, ok_inet, ok_vsock;
    if (argc != 2) { puts("PROBE fail usage"); return 2; }
    errno = 0; want = strtol(argv[1], &end, 10);
    if (errno || !end || *end || want <= 0 || want > 4095) { puts("PROBE fail bad-expected-errno"); return 2; }
    ok_unix = try_socket(AF_UNIX, &e_unix);
    ok_inet = try_socket(AF_INET, &e_inet);
    ok_vsock = try_socket(AF_VSOCK, &e_vsock);
    if (ok_unix && ok_inet && !ok_vsock && e_vsock == (int)want) { puts("PROBE ok"); return 0; }
    printf("PROBE fail unix=%d/%d inet=%d/%d vsock=%d/%d want=%ld\n", ok_unix, e_unix, ok_inet, e_inet, ok_vsock, e_vsock, want);
    return 1;
}
