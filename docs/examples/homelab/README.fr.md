> **Language / Langue**: [English](README.md) · **Français**

# Homelab d'exemple

Les exemples de la documentation s'exécutent sur les journaux d'audit de ce dossier, pour que chaque résultat montré dans une page puisse être reproduit et comparé avec la commande qui l'a produit.

## Le réseau

| Machine | Adresse | Journal d'audit | Rôle |
| :--- | :--- | :--- | :--- |
| `nas` | `192.168.1.10` | `logs/nas.csv` | Serveur de fichiers : SMB (445), démon rsync (873), SSH (22) |
| `pve` | `192.168.1.20` | `logs/pve.csv` | Hôte Proxmox : interface web (8006), SSH (22) ; sauvegarde vers `nas` chaque nuit |
| `pihole` | `192.168.1.2` | `logs/pihole.csv` | Serveur DNS du réseau (53), interface web (80) ; relaie en DNS sur TLS (853) vers `198.51.100.53` |
| `laptop` | `192.168.1.50` | aucun | Poste de travail : ouvre les partages de `nas`, les interfaces de `pve` et de `pihole`, une session SSH |

Les journaux couvrent la période du 2 au 9 novembre 2026. Les adresses publiques sont des adresses de documentation (`203.0.113.10`, `198.51.100.53`).

`portatlas.json` range les adresses dans trois groupes pour la matrice de règles par groupe : `dns`, `servers` et `clients`. Deux autres fichiers servent chacun à un exemple de la [référence de la ligne de commande](../../reference/cli.fr.md) : `filters.json`, une configuration qui écarte le port DNS, et `dns_cache.json`, un cache DNS qui nomme `198.51.100.53`.

## Lancer les exemples

Ouvrez un terminal dans ce dossier : PortAtlas lit `portatlas.json` dans le dossier de travail, et les commandes de la documentation nomment les journaux `logs`.

```bash
PortAtlas export rules -i logs -o rules.csv
```

`scenarios.json` liste les commandes citées par la documentation, et `output/<scénario>/` contient les fichiers que chacune écrit. La suite de tests exécute chaque scénario et échoue quand une sortie, ou un tableau cité dans une page, ne correspond plus à ce qu'écrit PortAtlas.

## Modifier un exemple

Après une modification relue d'une sortie, ou après l'ajout d'un scénario dans `scenarios.json`, lancez `scripts/Update-DocExamples.ps1` depuis la racine du dépôt. Il réécrit `output/` ; relisez son diff avant de commiter, car chaque changement à cet endroit est un changement que voient les lecteurs.
