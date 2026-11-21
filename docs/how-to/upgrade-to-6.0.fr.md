> **Language / Langue**: [English](upgrade-to-6.0.md) · **Français**

# Passer de la 5.x à la 6.0

PortAtlas 6.0 change ce qu'écrit `export rules` : le nom par défaut de la matrice de règles, les sorties refusées, les comptes et les noms de la matrice de paires et les règles des flux de bouclage. Elle cesse aussi de lire un journal d'audit par défaut dont le nom change à chaque démarrage de l'audit. Le journal d'audit, le cache DNS, les noms des commandes et les clés de configuration ne changent pas : `audit` relit les journaux et le cache écrits par la 5.x. Vérifiez les scripts qui lancent `export rules`, et ceux qui lancent `map`, `export` ou `resolve` sans `-i`, avant de déployer la 6.0.

## Avant la mise à jour

1. Listez les scripts et tâches planifiées qui lancent `export rules`, les programmes qui lisent ses matrices, et les commandes lancées sans `-i`.
2. Remplacez l'exécutable, puis suivez les sections ci-dessous.

## La matrice de règles s'appelle `portatlas_rules.csv`

**Ce qui change.** Sans `-o`, `export rules` écrit `portatlas_rules.csv` au lieu de `firewall_rules.csv`, comme les autres fichiers que PortAtlas nomme d'après le produit.

**Ce qu'il faut faire.** Dans chaque script qui lance `export rules` sans `-o` et lit `firewall_rules.csv`, lisez `portatlas_rules.csv` ou ajoutez `-o firewall_rules.csv`.

**Comment vérifier.** Lancez `export rules` sans `-o` : la ligne `Firewall rule matrix with <n> rule(s) successfully generated: <path>` nomme `portatlas_rules.csv`.

## Sorties qui désignent un journal d'audit ou le même fichier

**Ce qui change.** Les matrices sont écrites une fois tous les journaux lus. En 5.x, une option de sortie qui désignait l'un des journaux à lire remplaçait ce journal d'audit par une matrice sans message. En 6.0, la commande n'écrit rien et s'arrête avec le code de sortie 1 :

- quand une option de sortie désigne un journal d'audit à lire, donné par son propre chemin ou trouvé dans un dossier ou un motif : `The output '<path>' is also an audit log to read: choose another path for <option>.`
- quand deux options de sortie désignent le même fichier : `<option> and <option> name the same file '<path>': choose two different paths.`

Les noms de fichiers sont comparés sans tenir compte de la casse.

**Ce qu'il faut faire.** Donnez à chaque matrice son propre chemin, hors des fichiers lus par `--input`. Un script qui écrit les matrices dans le dossier qu'il lit avec `-i <dossier>` continue de fonctionner : une matrice n'est pas un journal d'audit, le dossier ne la propose donc pas en entrée. Chaque exécution affiche alors `Skipped <path>: its first line is not the header of an audit log.` pour chaque matrice, suivi de `A file whose own path is given to --input is always read.` : ces lignes ne sont pas des erreurs.

**Comment vérifier.** Lancez le script une fois : il se termine avec le code de sortie 0, sans message `choose` sur la sortie d'erreur.

## Comptes et noms de la matrice de paires

**Ce qui change.** `--pairs-output` est construite à partir de la nouvelle matrice de flux et non plus de la matrice de règles :

- `FlowCount` compte des flux. Un flux ne tient pas compte du processus et fusionne les règles relevées par les journaux de ses deux bouts. Une connexion relevée par les deux bouts, ou un port utilisé par plusieurs processus, donnait plusieurs lignes de règles en 5.x et était comptée plusieurs fois ; elle est désormais comptée une fois.
- `ObservationCount` compte chaque flux une fois, avec le plus grand compte de ses deux bouts, au lieu de les additionner.
- Une colonne `SeenFrom` est ajoutée à la fin de chaque ligne : les valeurs `SeenFrom` distinctes des flux de la paire, séparées par `, `, par exemple `Source, Both`.
- `SourceHost` et `DestinationHost` viennent de la matrice de flux : l'adresse d'un serveur audité prend le nom de ce serveur. En 5.x, ils venaient de la première règle de la paire : un nom peut donc changer.
- Une paire de bouclage donne désormais une ligne par serveur, au lieu d'une ligne pour tous les serveurs.

**Ce qu'il faut faire.** Un programme qui lit la matrice de paires par nom de colonne n'a rien à changer, sinon lire `SeenFrom` comme une liste. Un programme qui la lit par position doit accepter une colonne de plus à la fin. Une comparaison avec des matrices écrites par la 5.x doit s'attendre à un `FlowCount` plus faible partout où les deux bouts ont été audités ou plusieurs processus partagent un port.

**Comment vérifier.** Ouvrez la matrice de paires : la dernière colonne est `SeenFrom`.

## Les destinations de bouclage et non spécifiées donnent une règle par serveur

**Ce qui change.** Un flux de bouclage, ou une écoute sur `0.0.0.0` ou `::`, trouvé dans les journaux de deux serveurs partageait une règle en 5.x, au nom du serveur à l'observation la plus récente. Il donne désormais une règle par serveur, puisqu'une telle adresse ne désigne aucune machine. Une destination routable donne toujours une seule règle, quel que soit le journal qui l'a vue.

**Ce qu'il faut faire.** Attendez-vous à plus de lignes dans la matrice de règles et dans la matrice des écoutes quand plusieurs serveurs sont lus ensemble. Leurs numéros `RULE-` se décalent en conséquence : ne gardez pas de référence à un numéro de règle d'un export à l'autre.

**Comment vérifier.** Exportez les règles de deux serveurs avec `--listeners-only` : un service que les deux serveurs font écouter sur `0.0.0.0` a une ligne par serveur.

## Un journal d'audit par défaut nommé par `{Date}` ou `{Timestamp}` n'est plus lu

**Ce qui change.** Sans `-i`, `map`, `export rules`, `export ndjson` et `resolve` lisent le journal d'audit désigné par `OutputCsvPath`. En 5.x, `{Date}` dans ce chemin désignait le journal du jour courant (UTC), si bien qu'une commande lancée un autre jour ne trouvait aucun journal, et `{Timestamp}` désignait un journal qu'aucun audit n'avait écrit. En 6.0, avec l'un de ces jetons dans `OutputCsvPath`, ces commandes s'arrêtent aussitôt avec le code de sortie 1 : `OutputCsvPath '<value>' names a new audit log at each audit start, so it names no log to read: give the logs with --input, for example --input "<pattern>".`, où `<pattern>` est le chemin avec `*` à la place des jetons. `resolve` ne s'arrête que si ni `-i` ni `--cache` n'est donné.

**Ce qu'il faut faire.** Dans chaque script qui s'appuie sur un chemin `{Date}` pour lire le journal du jour, passez ce journal avec `-i`, ou passez le motif donné par le message pour lire tous les journaux.

**Comment vérifier.** Lancez le script une fois : il se termine avec le code de sortie 0, sans message `OutputCsvPath` sur la sortie d'erreur.
