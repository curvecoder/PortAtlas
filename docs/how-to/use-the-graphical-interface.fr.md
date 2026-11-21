> **Language / Langue**: [English](use-the-graphical-interface.md) · **Français**

# Utiliser l'interface graphique

L'interface graphique remplit les options d'une commande à partir d'un formulaire, affiche la ligne de commande qu'elle construit et la lance avec l'exécutable `PortAtlas`. Elle propose `map`, `export rules`, `export ndjson`, `resolve`, `lookup` et `config init` ; la commande `audit` se lance depuis un terminal (voir [Premiers pas](../getting-started.fr.md)). Le rôle de chaque option est décrit dans la [référence de la CLI](../reference/cli.fr.md) ; le formulaire affiche la même description sous chaque option.

## Installer

Sous Windows, téléchargez `PortAtlas-<version>-win-x64.zip` et `PortAtlas.Gui-<version>-win-x64.zip` depuis la [page des versions](https://github.com/curvecoder/PortAtlas/releases) et décompressez les deux dans le même dossier, par exemple `C:\Tools\PortAtlas`. Les deux archives contiennent `README.md`, `README.fr.md`, `LICENSE` et `portatlas.json` : quand la seconde le demande, remplacez-les, ce sont les mêmes fichiers. Lancez `PortAtlas.Gui.exe`.

Les versions Linux ne contiennent pas l'interface ; le [README](../../README.fr.md#installer) donne la commande qui la compile. Placez le `PortAtlas.Gui` compilé à côté de l'exécutable `PortAtlas`.

L'interface lance le premier de ces fichiers qui existe :

1. l'exécutable choisi avec **Change...** ou **Select PortAtlas...** ;
2. `PortAtlas.exe` (Linux : `PortAtlas`) dans le dossier de l'interface ;
3. le fichier désigné par la variable d'environnement `PORTATLAS_EXE`.

Quand aucun n'existe, un bandeau en haut de la fenêtre affiche `PortAtlas.exe was not found next to the GUI or at the configured path.` (Linux : `PortAtlas was not found …`) avec un bouton **Select PortAtlas...**. Le reste de la fenêtre fonctionne, mais **Run** échoue avec un message qui nomme tous les emplacements cherchés. La ligne **Executable**, au-dessus de la ligne de commande, montre le fichier utilisé ; **Change...** en choisit un autre, et **Automatic**, affiché tant qu'un fichier est choisi, revient à la recherche ci-dessus.

## La fenêtre

| Zone | Contenu |
| :--- | :--- |
| Barre du haut | Liste **Profile** avec **Load**, zone du nom de profil avec **Save**, bouton du thème. Les erreurs sur les profils et les préférences s'affichent dans cette barre, à côté du titre. |
| **Commands** (à gauche) | Les commandes que l'interface peut lancer. Les valeurs saisies dans un formulaire sont conservées quand on passe à une autre commande puis qu'on y revient. |
| Formulaire (au centre) | La description de la commande, ses arguments, puis ses options par sections. |
| **Executable** | L'exécutable utilisé, **Change...**, **Automatic**. |
| **Command line** | La ligne de commande construite à partir du formulaire, qui commence par le nom de la commande (sans `PortAtlas`), mise à jour après chaque modification, et **Copy**, qui la place dans le presse-papiers. |
| Barre d'exécution | **Run**, **Cancel**, l'état de l'exécution (`Idle`, `Running`, `Completed`, `Cancelled`, `Failed`), `Exit code: <n>` quand la commande s'est terminée d'elle-même, et un message quand l'exécution a été annulée ou que l'exécutable n'a pas pu être lancé. |
| Sortie (en bas) | Les lignes écrites par la commande ; celles de la sortie d'erreur s'affichent en rouge. Une nouvelle exécution les efface. |

## Remplir un formulaire

Les options sont regroupées en sections, affichées dans cet ordre et seulement quand la commande a des options dedans : `Inputs`, `Filters`, `MapRendering`, `Outputs`, `Resolution`, puis une dernière section pour les autres options, comme `--config`. Chaque section se replie et se déplie depuis son en-tête ; la dernière démarre repliée.

| Champ | Usage |
| :--- | :--- |
| Case à cocher | Une option sans valeur, comme `--listeners-only`. |
| Zone de texte | Une valeur. Pour un chemin, **Browse...** ouvre la boîte de dialogue de fichiers : une boîte d'ouverture pour un fichier lu, une boîte d'enregistrement pour un fichier écrit. |
| Zone de texte d'un entier | Un entier, comme `--min-observations`. Une valeur qui n'est pas un entier ou qui est hors limites est refusée par `PortAtlas` au lancement, avec son message et son code de sortie habituels. |
| Liste | Une des valeurs que `PortAtlas` accepte, chacune avec sa description. **Clear** revient à aucune valeur. |
| Zone de texte avec **Add** | Une option qui prend plusieurs valeurs, comme `--input`. **Add** déplace la valeur saisie dans la liste en dessous ; **Remove** retire une valeur. Pour `--input` et `--cache`, **File...** et **Folder...** ajoutent un fichier ou un dossier. Une valeur laissée dans la zone sans appuyer sur **Add** compte quand même. |

Une option laissée vide n'ajoute rien à la ligne de commande : `PortAtlas` applique alors sa valeur par défaut ou celle du fichier de configuration. `--config` a une seule valeur pour toutes les commandes : la saisir dans un formulaire la renseigne dans les autres.

Un chemin relatif est relatif au dossier de travail de l'interface : son propre dossier quand elle est lancée depuis l'explorateur de fichiers. **Browse...**, **File...** et **Folder...** donnent des chemins complets.

## Lancer une commande

**Run** lance la commande affichée sous **Command line**. L'interface passe chaque valeur comme un argument distinct, sans shell : les espaces et les guillemets d'une valeur n'ont pas besoin d'être échappés. La commande se comporte et se termine comme dans un terminal, avec les mêmes messages et les mêmes [codes de sortie](../reference/cli.fr.md) ; l'état `Completed` signifie seulement que la commande est terminée, et `Exit code: 0` qu'elle a réussi.

**Cancel** arrête la commande comme `Ctrl+C` dans un terminal : `resolve` enregistre les adresses déjà résolues avant de s'arrêter. Une commande qui tourne encore 10 secondes plus tard est arrêtée de force. Fermer la fenêtre pendant une exécution l'annule de la même façon, et la fenêtre se ferme une fois la commande arrêtée. Une exécution annulée affiche `Cancelled`, sans code de sortie.

Aucune de ces commandes ne lit les tables de sockets ; aucune n'a donc besoin des droits d'administrateur, sauf pour écrire dans un dossier où le compte ne peut pas écrire : elles lisent des journaux d'audit et des caches, écrivent des fichiers et, pour `resolve` et `lookup`, envoient des requêtes DNS et RDAP.

## Exemple : dessiner une carte à partir d'un journal d'audit

1. Dans **Commands**, sélectionnez `map`.
2. Dans `Inputs`, sur la ligne `--input`, cliquez sur **File...** et choisissez `portatlas_audit.csv`.
3. Dans `Outputs`, sur la ligne `--html`, cliquez sur **Browse...** et indiquez `homelab.html`.
4. Vérifiez la ligne de commande, par exemple `map --input C:\Tools\PortAtlas\portatlas_audit.csv --html C:\Tools\PortAtlas\homelab.html`.
5. Cliquez sur **Run**. La sortie affiche `Topology map generated: …`, et la barre d'exécution `Completed` et `Exit code: 0`.
6. Ouvrez `homelab.html` dans un navigateur.

## Enregistrer et charger des profils

Un profil conserve les valeurs de tous les formulaires, et l'exécutable choisi avec **Change...** s'il y en a un, pour relancer plus tard un ensemble de commandes.

- **Save** enregistre le profil sous le nom saisi dans la zone **Profile name**, ou met à jour le profil sélectionné quand la zone est vide. Remplacer un profil existant demande une confirmation. Un nom ne peut pas être vide, commencer ou finir par une espace, ni contenir un caractère qu'un nom de fichier ne peut pas contenir sur ce système (sous Windows : `\ / : * ? " < > |`).
- **Load** ouvre le profil sélectionné dans la liste, ou celui dont le nom est saisi quand aucun n'est sélectionné. Un profil sans exécutable choisi revient à la recherche automatique.
- Le dernier profil enregistré ou chargé s'ouvre de nouveau au démarrage suivant.

Les profils sont des fichiers JSON dans `Data\profiles` (Linux : `Data/profiles`), dans le dossier de l'interface. Quand ce dossier ne peut pas être créé, comme sous `C:\Program Files`, ils sont conservés dans le dossier de l'utilisateur : `%APPDATA%\PortAtlas\profiles` sous Windows, `$XDG_CONFIG_HOME/portatlas/profiles` (ou `~/.config/portatlas/profiles`) sous Linux. Un profil qui ne peut pas être lu, enregistré ou chargé affiche un message d'une ligne dans la barre du haut ; le formulaire garde ses valeurs.

## Changer de thème

Le bouton du thème affiche `Theme: System`, `Theme: Light` ou `Theme: Dark` ; chaque clic passe au suivant. Le thème et le dernier profil sont conservés dans `preferences.json`, un niveau au-dessus du dossier des profils (`Data`, ou `%APPDATA%\PortAtlas`, `$XDG_CONFIG_HOME/portatlas`, `~/.config/portatlas`). Un `preferences.json` absent ou endommagé donne les valeurs par défaut : `System`, et aucun profil chargé.

Pour la construction de l'interface et la raison pour laquelle elle n'a pas de descriptions d'options propres, voir [Comment l'interface graphique est construite](../explanation/gui-architecture.fr.md).
