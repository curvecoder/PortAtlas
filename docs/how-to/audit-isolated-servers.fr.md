> **Language / Langue**: [English](audit-isolated-servers.md) · **Français**

# Auditer des serveurs sans accès Internet

Objectif : auditer des serveurs qui n'atteignent pas Internet, puis nommer leurs adresses distantes depuis une machine connectée à Internet.

## Sur chaque serveur isolé

Lancez l'audit sans requêtes RDAP, qui ne feraient qu'expirer :

```bash
PortAtlas audit --no-tui --no-rdap
```

Ou mettez `"ResolveIpOwners": false` dans le fichier de configuration. Le DNS inverse reste actif et nomme les adresses connues du serveur DNS local. Pour n'envoyer aucune requête de nom, utilisez plutôt `--no-dns`.

Collectez les journaux d'audit et les fichiers de cache DNS (`portatlas_dns_cache.json`, ou `DnsCacheFilePath`) de chaque serveur.

## Sur une machine connectée à Internet

Fusionnez les caches, résolvez les adresses encore sans nom et écrivez des copies enrichies des journaux :

```bash
PortAtlas resolve -i "collected/*.csv" --cache "collected/*.json" --output-cache master_cache.json
```

Chaque `audit_x.csv` donne un `audit_x_enriched.csv` ; `--in-place` remplace plutôt les originaux. Les requêtes RDAP sont espacées de 200 ms (`--rdap-delay-ms`).

Relancer la commande enrichit de nouveau les originaux et laisse les copies de côté : un fichier `_enriched` trouvé à côté de son original est nommé à l'écran et n'est pas lu. Le dossier peut ensuite être donné tel quel à `map` ou à `export rules`, qui lisent la copie enrichie de chaque journal.

## Compléter le cache depuis plusieurs machines

Pour remplir le cache maître sans toucher aucun journal d'audit, donnez le cache seul. Seules les entrées sans nom sont résolues, et un nom déjà connu n'est jamais effacé :

```bash
PortAtlas resolve --cache master_cache.json
```

Ajoutez `--force-resolve` pour résoudre de nouveau chaque entrée. Une recherche qui ne trouve rien garde le nom résolu auparavant : lancer la commande depuis des machines qui atteignent des serveurs DNS différents ne fait donc que compléter le cache.

## Sans aucun accès réseau

Pour n'utiliser que les caches collectés, sans requête DNS ni RDAP :

```bash
PortAtlas resolve -i "collected/*.csv" --cache master_cache.json --cache-only
```

## Venir de la 1.x

Le mode isolé de la 1.x n'existe plus. Un fichier de configuration avec `"IsolatedMode": true` arrête PortAtlas avec une erreur tant que la clé n'est pas remplacée par `"ResolveIpOwners": false` ([guide de mise à jour](upgrade-to-2.0.fr.md)).
