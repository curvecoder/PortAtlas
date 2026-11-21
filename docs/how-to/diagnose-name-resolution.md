> **Language / Langue**: **English** · [Français](diagnose-name-resolution.fr.md)

# Diagnose a missing host name

Goal: understand why an address appears without a name in the audit, and fix it.

## Run the diagnostic

```bash
PortAtlas lookup 192.168.1.25
```

The report has six steps: address classification, local cache entry, DNS settings of the operating system, reverse DNS query with its duration and error, RDAP query (public addresses only), and the resulting cache entry.

## When the DNS server is slow

Step 4 shows the call duration and the timeout used, the same as the audit (`DnsResolutionTimeoutMs`, 3000 ms by default). If it reports `TIMEOUT`, or a duration close to the timeout, raise the timeout in the configuration file or on the command line:

```bash
PortAtlas audit --dns-timeout-ms 6000
```

Check the new value with `PortAtlas lookup 192.168.1.25 --dns-timeout-ms 6000`. Resolution runs in the background, so a longer wait does not delay the scans. An address whose query timed out is asked again one minute later; an address cached without a name (step 2 shows an empty `HostName` with `Source: None`) is asked again 24 hours after its `Resolved at` date.

## When RDAP is slow

Step 5 shows the RDAP call duration and the timeout used, the same as the audit and `resolve` (`RdapTimeoutMs`, 4000 ms by default). `rdap.org` redirects each query to the registry that holds the address, so a lookup opens two HTTPS connections. If step 5 reports `TIMEOUT`, or a duration close to the timeout, raise `RdapTimeoutMs` in the configuration file. An address without DNS name whose RDAP query got no answer is not cached: it is asked again 15 minutes later, even when the connection is gone, up to three times; after that, only a scan that sees the address again queues it. Restarting `audit` asks again for every logged flow still without name.

## Why ping -a finds a name and PortAtlas does not

On Windows, `ping -a` asks the hosts file, the DNS cache, NetBIOS, LLMNR and the DNS servers with the domain suffixes. PortAtlas sends only a reverse DNS query (a `PTR` record in `in-addr.arpa`). A machine known through NetBIOS or LLMNR but without a `PTR` record therefore stays unnamed. The reverse DNS step of `lookup` shows the error returned.

## Fix it

- Add the missing `PTR` record to the reverse DNS zone (router, Pi-hole…).
- Or declare the name in the configuration file:

  ```json
  {
    "CustomHostAliases": { "192.168.1.25": "nas" }
  }
  ```

  or on the command line: `PortAtlas audit --alias 192.168.1.25=nas`.

Aliases take precedence over DNS. `lookup` reads the aliases of the configuration file only: a pair given to `audit --alias` does not show in its report. Addresses that are never resolved (broadcast, multicast, link-local by default) are listed in `DnsExcludedNetworks`; for them, and for an address with an alias, `lookup` prints `SKIPPED` at the reverse DNS and RDAP steps.
