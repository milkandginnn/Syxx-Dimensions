#ifndef Ccage_h
#define Ccage_h

#include <stdint.h>

/// Datalink type constants (from pcap/bpf.h / pcap/dlt.h).
/// Parity: these values are mirrored (with a pinning test) by SyxxKit's
/// `DNSCapture.LinkLayerType`, which cannot import this header. Keep both in sync.
#define CAGE_DLT_NULL    0   // BSD loopback / NULL (4-byte AF header)
#define CAGE_DLT_EN10MB  1   // Ethernet (14-byte header)
#define CAGE_DLT_PPP     9   // PPP (4-byte address/control/protocol header; built-in L2TP/IPsec utun)
#define CAGE_DLT_RAW     12  // Raw IP, no link header (macOS value; note: 14 on some BSDs)
#define CAGE_DLT_PKTAP   149 // Apple per-packet metadata; DLT_PKTAP == DLT_USER2 on live macOS captures

/// Opaque handle to a pcap capture session.
typedef struct pcap PcapHandle;

/// Open a live capture on `device`.  Returns non-null on success; on failure
/// returns NULL and fills `errbuf` (PCAP_ERRBUF_SIZE = 256).
PcapHandle *cage_pcap_open_live(const char *device, int snaplen,
                                int promisc, int timeout_ms, char *errbuf);

/// Open a live capture on `device` requesting Apple PKTAP per-packet metadata (the
/// kernel-attributed originator pid/process each packet arrives with — what `tcpdump -k`
/// does). Uses pcap_create + Apple's private-but-exported pcap_set_want_pktap (resolved via
/// dlsym so a libpcap without it degrades cleanly) + pcap_activate. Returns non-null ONLY
/// when the activated handle's datalink is really DLT_PKTAP; on any failure returns NULL and
/// fills `errbuf`, so the caller can fall back to a plain open — never silently.
///
/// Instrumented live finding (2026-07-23, D107 Fix 1): opening the "pktap,<if>" pseudo-device
/// via plain pcap_open_live yields DLT_RAW (12) with NO metadata header — the pktap header is
/// only prepended when want_pktap is set before activation.
PcapHandle *cage_pcap_open_pktap(const char *device, int snaplen,
                                 int timeout_ms, char *errbuf);

/// Read the next packet (wraps pcap_next_ex).  Returns:
///   1  — packet read; *data and *len are set
///   0  — timeout (no packet within the read timeout)
///  -1  — error (use cage_pcap_geterr for details)
///  -2  — broken loop (should not happen with next_ex)
int cage_pcap_next_ex(PcapHandle *p, const uint8_t **data, int *len);

/// Return the error string for the last error on handle p.
const char *cage_pcap_geterr(PcapHandle *p);

/// Close a capture handle and free its resources.
void cage_pcap_close(PcapHandle *p);

/// Return a human-readable device name for capture (pcap_lookupdev).
/// Caller must free the returned string.
char *cage_pcap_lookupdev(char *errbuf);

/// Compile and apply a BPF filter expression to capture handle `p`.
/// Returns 0 on success; on failure fills `errbuf` and returns -1.
int cage_pcap_compile_filter(PcapHandle *p, const char *expr,
                              int optimize, uint32_t netmask, char *errbuf);

/// Return the datalink type of the capture handle (pcap_datalink).
int cage_pcap_datalink(PcapHandle *p);

#endif /* Ccage_h */
