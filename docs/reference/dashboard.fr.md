> **Language / Langue**: [English](dashboard.md) · **Français**

# Référence du tableau de bord terminal

`PortAtlas audit` affiche un tableau de bord plein écran dans le terminal, sauf si `--no-tui` est donné, si `EnableDashboard` vaut `false`, ou si l'entrée ou la sortie du processus est redirigée.

Sans tableau de bord, l'audit écrit ses messages sous forme de lignes dans la console. Lorsque l'entrée n'est pas redirigée, `Q` arrête toujours l'audit et `M` écrit toujours les cartes ; les autres touches sont sans effet.

## Disposition de l'écran

| Zone | Contenu |
| :--- | :--- |
| En-tête | `PortAtlas v<version>` : nom du serveur, système, durée de fonctionnement ; IP de l'hôte, intervalle d'analyse, horloge ; état, vue courante, nombre de sockets ignorés par les filtres |
| METRICS | Sockets actifs de la dernière analyse, flux uniques enregistrés, nouveaux flux de la dernière analyse |
| NETWORK TRAFFIC BREAKDOWN | Sockets TCP, suivis des sockets en écoute et sortants des deux protocoles ; sockets UDP ; sockets entrants |
| TOP ACTIVE PROCESSES (AUDITED), ou ACTIVE PROCESSES (SNAPSHOT) dans la vue Active Sockets | Les quatre processus qui ont le plus de sockets, avec une barre et leur nombre de sockets ; un processus sans nom compte comme `Unknown` |
| Tableau des flux | Ligne de titre, en-têtes de colonnes, une ligne par flux |
| Pied de page | Les touches disponibles dans l'état courant |

Les compteurs de trafic et de processus portent sur les sockets de la dernière analyse. Dans la vue Audit Log, les sockets exclus par les filtres n'y sont pas comptés.

Un terminal de moins de 100 colonnes sur 16 lignes n'affiche que `Window too small`, la taille courante et la taille minimale, et `[Q] Quit`. Le tableau de bord revient quand la fenêtre est agrandie.

## Vues

| Vue | Contenu |
| :--- | :--- |
| Audit Log | Les flux enregistrés par cet audit, du plus récent au plus ancien, suivis des flux rechargés depuis le journal d'audit au démarrage. Les flux exclus ne sont pas affichés. |
| Active Sockets | Tous les sockets vus par la dernière analyse, avant application des filtres d'enregistrement |

Seul un flux vu pour la première fois est ajouté à la vue Audit Log ; un flux revu garde sa place. La vue garde 100 lignes en mémoire, ou le nombre de flux rechargés qui restent après les filtres s'il est plus grand. Un nouveau flux au-delà de cette limite fait sortir de la vue la ligne la plus ancienne ; le journal d'audit sur disque la conserve. `L` et `A` relèvent la limite, mais une ligne déjà sortie ne revient pas.

## Tableau des flux

La ligne de titre nomme la vue, `AUDIT LOG (DISCOVERIES)` ou `ACTIVE SOCKETS (SNAPSHOT)`, suivie de `[Page <n>/<total> | Rows <première>-<dernière> of <nombre> | Max: <limite>]`. `Max` affiche `ALL` dès que la limite atteint 50 000 lignes, et `LIVE ACTIVE` dans la vue Active Sockets. `ALL` reste une limite : 50 000 lignes, ou le nombre de flux rechargés s'il est plus grand.

| Colonne | Contenu |
| :--- | :--- |
| `TIME` | Heure à laquelle le flux a été vu pour la première fois, en UTC (HH:mm:ss) ; dans la vue Active Sockets, heure de la dernière analyse. L'horloge de l'en-tête affiche l'heure locale. |
| `PROTO` | `TCP` ou `UDP` |
| `DIR` | `LIST` (en écoute), `IN` (entrant), `OUT` (sortant) |
| `LOCAL` | Adresse et port locaux |
| `REMOTE` | Nom d'hôte et port distants, ou adresse et port distants tant que le nom n'est pas résolu ; toujours l'adresse dans la vue Active Sockets |
| `PROCESS` | Nom du processus propriétaire |
| `TARGET / CMD` | Exécutable cible, ou la ligne de commande s'il n'y en a pas |

Une page contient autant de lignes que la hauteur de la fenêtre le permet, au moins 3.

### Messages dans la ligne de titre

Un message remplace les informations de page de la ligne de titre pendant au moins la durée ci-dessous ; il disparaît au rafraîchissement suivant (une analyse, une touche, un nom résolu) :

| Message | Affiché pendant |
| :--- | :--- |
| `Scan failed (<n> of 10 in a row): …` | 15 s. `n` va de 1 à 9 : la 10e analyse en échec d'affilée arrête l'audit, qui affiche `The audit stopped after 10 failed scans in a row: …` et se termine avec le code 1. |
| `Could not rewrite the audit log: …` | 15 s. Les flux restent en mémoire, et la réécriture suivante réessaie. |
| `Topology map not written: …` (`M`, ou la carte réécrite après chaque analyse quand `OutputHtmlMapPath` est défini) | 15 s |
| `No remote address to exclude …`, `No remote port to exclude …` | 15 s |
| Résultat d'une exclusion | 4 s |
| `Reloaded <n> historical flows from CSV` | 5 s |
| Changement de vue, `L`, `A` | 3 s |

Les avertissements commencent par `⚠`, les autres messages par `✔` ; le résultat d'une exclusion qui n'a pas pu être enregistrée commence aussi par `✔`. `M` n'affiche aucun message quand la carte est écrite. Sans tableau de bord, les avertissements sont écrits dans la console sous la forme `[HH:mm:ss] <message>`.

## Touches

| Touche | Action |
| :--- | :--- |
| `Q` | Arrêter l'audit et quitter |
| `C` | Redessiner l'écran |
| `V` ou `F` | Basculer entre les deux vues |
| `M` | Écrire la carte HTML (`OutputHtmlMapPath`, ou `portatlas_topology.html`), et le diagramme Draw.io lorsque `OutputDrawioPath` est défini |
| `↑` `↓` | Démarrer la sélection sur la première ligne de la page, puis la déplacer ; elle s'arrête à la première et à la dernière ligne de la page |
| `←` `→` | Démarrer la sélection sur la première ligne, cellule LOCAL, puis la déplacer d'une cellule à l'autre ; elle passe de TARGET à LOCAL |
| `N` ou `Page Down` | Page suivante |
| `P` ou `Page Up` | Page précédente |
| `L` ou `+` | Relever la limite de lignes de 100, jusqu'à 50 000 |
| `A` | Relever la limite de lignes à 50 000 |
| `Entrée`, `X` ou `E` | Exclure la valeur de la cellule sélectionnée ; rien sans sélection |
| `Échap` | Annuler la sélection |

`Ctrl+C` et `SIGTERM` arrêtent l'audit comme `Q`. Changer de vue ramène à la première page ; changer de page garde la sélection.

## Exclure depuis le tableau de bord

`←` et `→` déplacent la sélection entre cinq arrêts : LOCAL, REMOTE, REMOTE PORT, PROCESS, TARGET. Exclure une cellule applique la règle aussitôt à l'enregistrement et à l'affichage, retire de la vue Audit Log les lignes concernées, et ajoute la valeur au fichier de configuration. Les flux déjà enregistrés restent dans le journal d'audit et dans les cartes.

| Cellule sélectionnée | Valeur exclue | Clé mise à jour |
| :--- | :--- | :--- |
| LOCAL | Port local | `ExcludedLocalPorts` |
| REMOTE | Adresse distante (une adresse IPv6 qui encapsule une IPv4 est écrite en IPv4) | `ExcludedRemoteIps` |
| REMOTE PORT | Port distant | `ExcludedRemotePorts` |
| PROCESS | Nom du processus, et le nom sans son extension | `ExcludedExactNames` |
| TARGET | Nom de fichier de l'exécutable cible, sans son dossier, et le nom sans son extension ; le nom du processus quand la ligne n'a pas de cible | `ExcludedExactNames` |

La cellule REMOTE a deux arrêts : `→` sélectionne d'abord l'adresse, puis son port, seul mis en évidence, sous l'en-tête `[REMOTE PORT]`. Une adresse distante `0.0.0.0` ou `::`, ou un port distant 0, comme sur les lignes d'écoute et UDP, qui n'ont pas de pair, ne peut pas être exclu : le message invite à sélectionner LOCAL ou PROCESS.

Le fichier mis à jour est celui que l'audit a lu : le fichier donné par `--config`, ou `portatlas.json`, cherché dans le dossier de travail, puis à côté de l'exécutable. Quand l'audit a démarré sans fichier de configuration, la première exclusion crée `portatlas.json` dans le dossier de travail.

Seules les valeurs exclues sont ajoutées ; les autres clés et valeurs sont conservées, et le fichier est réécrit avec une indentation de deux espaces. Une valeur déjà présente, comparée sans tenir compte de la casse pour les adresses et les noms, n'est pas ajoutée deux fois. Quand le fichier n'a pas encore de clé pour cette liste, la clé est écrite avec ses valeurs par défaut suivies de la nouvelle, car une liste du fichier remplace la liste par défaut au démarrage suivant. Une valeur que l'audit exclut déjà (par le fichier, les valeurs par défaut ou la ligne de commande) ne provoque aucun enregistrement. Chaque enregistrement écrit toutes les exclusions ajoutées pendant l'exécution : une exclusion retirée du fichier à la main pendant l'audit revient à l'enregistrement suivant.

La nouvelle version est d'abord écrite à côté du fichier, sous son nom suivi de `.tmp` puis de `.pending`, et ne le remplace qu'ensuite : un programme qui lit le fichier obtient l'ancienne version ou la nouvelle, jamais un mélange des deux, et une interruption laisse l'ancien fichier intact. Windows refuse le remplacement tant qu'un autre programme tient le fichier ouvert ; l'enregistrement compte alors comme non fait, et le fichier `.pending` reste jusqu'à ce que l'enregistrement suivant le remplace. Si le fichier lui-même manque alors qu'un fichier `.pending` est présent, l'enregistrement suivant part de la version `.pending`.

Lorsque le fichier ne peut pas être écrit (en cours d'utilisation, en lecture seule, accès refusé), le message se termine par `not saved to <file>: the file is in use or cannot be written`. Lorsque le fichier n'est pas un objet JSON, contient une clé deux fois, ou contient la liste sous un autre type, il se termine par `not saved to <file>: the file is no longer a valid configuration`. Dans les deux cas le fichier est laissé tel quel, et la cause reste dans les messages des exclusions suivantes jusqu'à un enregistrement réussi. L'exclusion s'applique tout de même jusqu'à l'arrêt de l'audit, et la prochaine exclusion qui peut être enregistrée écrit aussi les précédentes.

## Masquage pour les captures d'écran

Ces options ne changent que ce qu'affiche le tableau de bord ; le journal d'audit, les cartes et l'en-tête console d'un audit sans tableau de bord gardent les vraies valeurs.

| Option | Clé de configuration | Effet |
| :--- | :--- | :--- |
| `--mask-server <name>` | `MaskServerName` | Nom du serveur affiché dans l'en-tête |
| `--mask-ip <address>` | `MaskLocalIp` | IP de l'hôte affichée dans l'en-tête, et adresse locale d'une ligne quand c'est l'adresse principale ou `0.0.0.0` |
| `--obfuscate` | aucune | Affiche `HOMELAB-01` et `192.168.1.10`, à la place des clés de configuration aussi, sauf si `--mask-server` ou `--mask-ip` donne une autre valeur |

L'en-tête coupe le nom du serveur et l'IP de l'hôte à 10 caractères (`192.168.1…`). Les autres adresses locales, les adresses et noms d'hôte distants, les noms de processus et les lignes de commande sont affichés tels quels.
