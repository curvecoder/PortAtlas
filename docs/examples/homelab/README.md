> **Language / Langue**: **English** · [Français](README.fr.md)

# Example homelab

The examples of the documentation run on the audit logs of this folder, so that each result shown in a page can be reproduced and compared with the command that produced it.

## The network

| Machine | Address | Audit log | Role |
| :--- | :--- | :--- | :--- |
| `nas` | `192.168.1.10` | `logs/nas.csv` | File server: SMB (445), rsync daemon (873), SSH (22) |
| `pve` | `192.168.1.20` | `logs/pve.csv` | Proxmox host: web interface (8006), SSH (22); backs up to `nas` every night |
| `pihole` | `192.168.1.2` | `logs/pihole.csv` | DNS server for the network (53), web interface (80); forwards DNS over TLS (853) to `198.51.100.53` |
| `laptop` | `192.168.1.50` | none | Workstation: opens the shares of `nas`, the interfaces of `pve` and `pihole`, one SSH session |

The logs cover 2 to 9 November 2026. Public addresses are documentation addresses (`203.0.113.10`, `198.51.100.53`).

`portatlas.json` puts the addresses in three groups for the group rule matrix: `dns`, `servers` and `clients`. Two more files serve single examples of the [command-line reference](../../reference/cli.md): `filters.json`, a configuration that removes the DNS port, and `dns_cache.json`, a DNS cache that names `198.51.100.53`.

## Run the examples

Open a terminal in this folder: PortAtlas reads `portatlas.json` from the working directory, and the commands of the documentation name the logs as `logs`.

```bash
PortAtlas export rules -i logs -o rules.csv
```

`scenarios.json` lists the commands quoted by the documentation, and `output/<scenario>/` holds the files each one writes. The test suite runs every scenario and fails when an output, or a table quoted in a page, no longer matches what PortAtlas writes.

## Change an example

After a reviewed change of an output, or after adding a scenario to `scenarios.json`, run `scripts/Update-DocExamples.ps1` from the root of the repository. It rewrites `output/`; read its diff before committing, since every change there is a change the readers see.
