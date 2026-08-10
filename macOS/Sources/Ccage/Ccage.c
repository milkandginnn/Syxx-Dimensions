#include "Ccage.h"
#include <dlfcn.h>
#include <pcap/pcap.h>
#include <stdio.h>
#include <string.h>

PcapHandle *cage_pcap_open_live(const char *device, int snaplen,
                                int promisc, int timeout_ms, char *errbuf) {
    return pcap_open_live(device, snaplen, promisc, timeout_ms, errbuf);
}

PcapHandle *cage_pcap_open_pktap(const char *device, int snaplen,
                                 int timeout_ms, char *errbuf) {
    // Apple's shipped libpcap exports pcap_set_want_pktap (tcpdump -k relies on it) but the
    // SDK header does not declare it. Resolve at runtime so a future libpcap without the
    // symbol degrades to the caller's plain-open fallback instead of failing to link.
    int (*set_want_pktap)(pcap_t *, int) =
        (int (*)(pcap_t *, int))dlsym(RTLD_DEFAULT, "pcap_set_want_pktap");
    if (set_want_pktap == NULL) {
        snprintf(errbuf, PCAP_ERRBUF_SIZE, "pcap_set_want_pktap not exported by libpcap");
        return NULL;
    }
    pcap_t *p = pcap_create(device, errbuf);
    if (p == NULL) {
        return NULL;
    }
    // Immediate mode is required for the per-packet live loop: without it the activated
    // handle buffers until the BPF store fills, and a short capture window ends with real
    // packets still undelivered (observed live 2026-07-23: 14 quiet seconds, one burst,
    // concurrent dig traffic lost entirely).
    if (pcap_set_snaplen(p, snaplen) != 0 ||
        pcap_set_timeout(p, timeout_ms) != 0 ||
        pcap_set_immediate_mode(p, 1) != 0 ||
        set_want_pktap(p, 1) != 0) {
        snprintf(errbuf, PCAP_ERRBUF_SIZE, "pcap pktap pre-activation setup failed: %s",
                 pcap_geterr(p));
        pcap_close(p);
        return NULL;
    }
    if (pcap_activate(p) < 0) {   // negative = error; positive = warning (usable)
        snprintf(errbuf, PCAP_ERRBUF_SIZE, "pcap_activate: %s", pcap_geterr(p));
        pcap_close(p);
        return NULL;
    }
    // Honesty gate: only claim pktap mode when the kernel actually delivers the metadata
    // header. A handle that activated but presents another DLT would silently break the
    // caller's attribution assumption (the exact failure the 2026-07-23 instrumented run hit
    // with the pcap_open_live route).
    if (pcap_datalink(p) != CAGE_DLT_PKTAP) {
        snprintf(errbuf, PCAP_ERRBUF_SIZE,
                 "pktap requested but datalink is %d, not DLT_PKTAP", pcap_datalink(p));
        pcap_close(p);
        return NULL;
    }
    return p;
}

int cage_pcap_next_ex(PcapHandle *p, const uint8_t **data, int *len) {
    // D329: initialize output pointers before calling pcap_next_ex. When pcap_next_ex
    // returns non-1 (timeout, error, or savefile EOF), *data and *len would otherwise
    // remain uninitialized, risking reads of stale pointer values in the Swift caller.
    *data = NULL;
    *len = 0;
    struct pcap_pkthdr *header;
    const uint8_t *pkt;
    int ret = pcap_next_ex(p, &header, &pkt);
    if (ret == 1) {
        *data = pkt;
        // Captured length, not original wire length. The pktap pseudo-interface prepends a ~156-byte
        // metadata header to every packet, which eats into the snaplen budget, so a large DNS answer
        // can be truncated. The buffer holds only caplen bytes; the Swift parser indexes into it, so
        // reporting header->len (the full untruncated size) would invite reads past the captured data.
        *len = header->caplen;
    }
    return ret;
}

// D364: POINTER LIFETIME CONTRACT — the returned pointer is valid only until the next
// pcap_* call on the same handle (pcap_next_ex, pcap_compile, pcap_close, etc.).
// Callers MUST copy or use the string BEFORE making any subsequent pcap call on `p`.
// The Swift bridge (SyxxCage.swift) converts immediately via String(cString:), which
// satisfies this contract. Do NOT cache the returned pointer across pcap calls.
const char *cage_pcap_geterr(PcapHandle *p) {
    return pcap_geterr(p);
}

void cage_pcap_close(PcapHandle *p) {
    pcap_close(p);
}

char *cage_pcap_lookupdev(char *errbuf) {
    // pcap_lookupdev() is deprecated AND returns a pointer to a static internal buffer that must
    // NOT be freed — the previous version returned it directly while the header promised the caller
    // must free(), so SyxxCage's free() corrupted the heap and crashed on every launch. Use
    // findalldevs and return a heap-allocated (strdup) copy so the documented free() contract holds.
    pcap_if_t *alldevs = NULL;
    if (pcap_findalldevs(&alldevs, errbuf) != 0 || alldevs == NULL) {
        return NULL;
    }
    char *name = NULL;
    // Prefer the ACTIVE NIC: up, running, non-loopback, and carrying an assigned address. Just
    // "first non-loopback" picks dead pseudo-interfaces (awdl0/ap1/utun) that see no traffic.
    for (pcap_if_t *d = alldevs; d != NULL; d = d->next) {
        if (d->flags & PCAP_IF_LOOPBACK) continue;
        if ((d->flags & PCAP_IF_UP) && (d->flags & PCAP_IF_RUNNING) && d->addresses != NULL) {
            name = strdup(d->name);
            break;
        }
    }
    if (name == NULL) {                          // fallback: any non-loopback device
        for (pcap_if_t *d = alldevs; d != NULL; d = d->next) {
            if (!(d->flags & PCAP_IF_LOOPBACK)) { name = strdup(d->name); break; }
        }
    }
    // D365: refuse to fall back to a loopback-only device. Loopback captures no real DNS
    // traffic (queries to 127.0.0.1:53 are local-only and do not represent network DNS).
    // Returning a loopback device would produce an empty capture that reads as "no DNS
    // activity" when in fact the system has no usable capture interface. Return NULL so the
    // Swift caller reports "no capture device available" instead of silently capturing nothing.
    pcap_freealldevs(alldevs);
    return name;
}

int cage_pcap_compile_filter(PcapHandle *p, const char *expr,
                              int optimize, uint32_t netmask, char *errbuf) {
    // D366: validate inputs before passing to pcap_compile. A NULL expr would segfault
    // inside pcap_compile. The Swift caller always passes a non-null literal, but the C API
    // must be defensive — a future caller or FFI bridge could pass NULL.
    if (p == NULL || expr == NULL) {
        snprintf(errbuf, PCAP_ERRBUF_SIZE, "cage_pcap_compile_filter: NULL %s",
                 p == NULL ? "handle" : "expression");
        return -1;
    }
    struct bpf_program fp;
    if (pcap_compile(p, &fp, expr, optimize, netmask) == -1) {
        snprintf(errbuf, PCAP_ERRBUF_SIZE, "pcap_compile: %s", pcap_geterr(p));
        return -1;
    }
    if (pcap_setfilter(p, &fp) == -1) {
        snprintf(errbuf, PCAP_ERRBUF_SIZE, "pcap_setfilter: %s", pcap_geterr(p));
        pcap_freecode(&fp);
        return -1;
    }
    pcap_freecode(&fp);
    return 0;
}

int cage_pcap_datalink(PcapHandle *p) {
    return pcap_datalink(p);
}
