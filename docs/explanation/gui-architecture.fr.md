> **Language / Langue**: [English](gui-architecture.md) · **Français**

# Comment l'interface graphique est construite

L'interface graphique permet de remplir un formulaire au lieu de saisir les options d'une commande. Cette page explique son organisation et pourquoi elle ne décrit pas elle-même les options. La décision est consignée dans l'[ADR-0002](../adr/0002-avalonia-gui-generated-from-command-tree.md). Les options elles-mêmes sont dans la [référence de la CLI](../reference/cli.fr.md). Pour installer l'interface et s'en servir, voir [Utiliser l'interface graphique](../how-to/use-the-graphical-interface.fr.md).

## Une seule source pour les options

Les options de chaque commande sont définies une seule fois, dans l'arbre de commandes que la CLI construit pour l'analyse et pour l'aide. L'interface lit cet arbre et génère un formulaire par commande : une nouvelle option apparaît dans le formulaire sans code d'interface. L'arbre ne dit pas deux choses, qu'une table indexée par nom d'option fournit : la section du formulaire où l'option se place, et si une option de chemin désigne un fichier à lire, un fichier à écrire ou un dossier. Un test échoue lorsqu'une option n'a pas d'entrée dans cette table, ou qu'une entrée nomme une option qui n'existe plus.

Les ensembles fermés de valeurs (par exemple les dispositions de la carte) sont exposés par la CLI elle-même comme valeurs de complétion de l'option : le formulaire liste exactement ce que la CLI accepte. Les valeurs minimales ne sont pas recopiées dans le formulaire : la CLI valide la ligne de commande, et le formulaire affiche son message et son code de sortie tels quels.

## Commandes du formulaire

Le formulaire propose les commandes qui s'exécutent une fois puis s'arrêtent : `map`, `export rules`, `export ndjson`, `resolve`, `lookup` et `config init`. La commande `audit` s'exécute jusqu'à son arrêt et ne fait pas partie de la première version de l'interface.

## Modèles de vue et abstractions

Chaque descripteur de commande est transformé en un `CommandFormViewModel` qui regroupe ses modèles de vue d'option (`FlagOptionViewModel`, `TextOptionViewModel`, `IntegerOptionViewModel`, `ChoiceOptionViewModel`, `TextListOptionViewModel`) dans l'ordre de `OptionGroup`, sans les groupes vides, et met à jour l'aperçu de la ligne de commande à chaque modification. `MainViewModel` conserve une instance de formulaire par commande afin que les valeurs saisies pour une commande restent présentes lorsqu'on change de commande puis qu'on y revient. `RunViewModel` gère l'état d'exécution (`Idle`, `Running`, `Completed`, `Cancelled`, `Failed`), diffuse les lignes de sortie via `IUiDispatcher` et désactive la commande de lancement tant qu'une exécution est en cours. Fermer la fenêtre principale pendant une exécution l'arrête d'abord (`RunViewModel.StopAsync`) et ferme la fenêtre une fois l'exécution terminée : aucun processus de la CLI ne survit à l'interface. Pour arrêter une exécution, par le bouton **Cancel** ou en fermant la fenêtre, le lanceur ferme l'entrée standard de la CLI, ce qui [arrête la commande comme `Ctrl+C`](../reference/cli.fr.md#arrêt-par-lentrée-standard) : `resolve` enregistre les adresses déjà résolues. Le lanceur ne tue l'arbre de processus de la CLI que s'il tourne encore 10 secondes plus tard. `ProfilesViewModel` liste, enregistre et charge les profils nommés.

Les modèles de vue résident dans `PortAtlas.Gui.Core` et n'accèdent au système d'exploitation et à la bibliothèque graphique qu'à travers des abstractions : `IPortAtlasRunner` et `IPortAtlasExecutableLocator` pour l'exécution du processus, `IFilePickerService` pour les boîtes de dialogue de fichiers et de dossiers, `IUiDispatcher` pour renvoyer les lignes de sortie sur le fil de l'interface, `IConfirmationService` pour les questions oui / non, et `IProfileDirectoryProvider` pour déterminer le dossier des profils.

## Exécution d'une commande

L'état du formulaire, qui contient les valeurs saisies pour une commande, est converti en liste d'arguments : le chemin de la commande, puis les options renseignées, puis les arguments positionnels. Une option laissée vide n'ajoute rien, et la CLI applique alors son défaut. Une option répétable est écrite une fois par valeur. Un aperçu montre la ligne de commande avec des guillemets pour l'affichage et la copie, mais le programme n'exécute jamais ce texte.

La commande s'exécute comme processus enfant de l'exécutable `PortAtlas`, trouvé comme décrit dans [Utiliser l'interface graphique](../how-to/use-the-graphical-interface.fr.md#installer). Le processus reçoit directement le tableau d'arguments, sans shell : aucune valeur ne peut être interprétée comme une syntaxe de shell. La sortie standard et la sortie d'erreur sont lues ligne par ligne pendant l'exécution. Annuler une exécution ferme d'abord l'entrée standard, comme décrit plus haut, et ne tue l'arbre de processus que s'il tourne encore 10 secondes plus tard.

L'exécution de l'exécutable donne le même comportement que la CLI, codes de sortie compris, et l'échec d'une commande ne peut pas faire tomber l'interface.

## L'exécutable PortAtlas

L'archive de l'interface ne contient pas l'exécutable `PortAtlas` ; [Utiliser l'interface graphique](../how-to/use-the-graphical-interface.fr.md#installer) explique comment installer les deux et dans quel ordre l'interface cherche l'exécutable. La ligne à côté de **Executable** montre le chemin trouvé au démarrage ou après le dernier changement de chemin (**Change...**, **Automatic**, chargement d'un profil), mais chaque exécution cherche à nouveau, et un chemin choisi qui ne contient plus de fichier est ignoré sans message. Le chemin choisi est enregistré dans le profil, afin que des profils différents puissent lancer des versions différentes de PortAtlas.

## Profils

Un profil est un ensemble nommé d'états de formulaire, un par commande, stocké dans un fichier JSON sous `Data\profiles`, à côté de l'exécutable. Lorsque ce dossier ne peut pas être créé (installation sous Program Files), les profils restent dans le dossier utilisateur des versions précédentes : `%APPDATA%\PortAtlas\profiles` sous Windows, `$XDG_CONFIG_HOME/portatlas/profiles` (ou `~/.config/portatlas/profiles`) sous Linux. À la première création de `Data\profiles`, les profils et `preferences.json` du dossier utilisateur y sont copiés et laissés en place. La copie ne devient `Data\profiles` qu'une fois complète : si elle s'arrête en cours de route (un profil tenu ouvert par un autre programme), l'application utilise le dossier utilisateur pour cette exécution et recopie au démarrage suivant. Un fichier est écrit sous un nom temporaire puis renommé par-dessus le précédent : une interruption laisse le profil antérieur intact. Un fichier de profil illisible provoque une erreur qui nomme le fichier.

Écraser un profil existant demande toujours une confirmation via `IConfirmationService`, qui sort la question du modèle de vue et permet aux tests d'y répondre. Le thème de couleurs et le nom du dernier profil utilisé sont stockés dans `preferences.json`, un niveau au-dessus du dossier des profils utilisé : ils suivent les profils quand ceux-ci restent dans le dossier utilisateur ; un fichier de préférences absent ou endommagé retombe sur les valeurs par défaut.

Les options déclarées une seule fois sur la commande racine, comme `--config`, partagent une même valeur pour toutes les commandes.

## Couches

| Projet | Rôle |
| :--- | :--- |
| `PortAtlas.Gui.Core` | Catalogue des commandes, présentation des options, état du formulaire, liste d'arguments, exécuteur, profils, modèles de vue et abstractions d'interface. Aucune dépendance à la bibliothèque d'interface. |
| `PortAtlas.Gui` | Vues Avalonia, adaptateur des sélecteurs de fichiers, répartiteur du fil d'interface et fournisseur du dossier des profils. |
| `PortAtlas.Gui.Tests` | Tests unitaires, tests d'interface sans écran et tests d'architecture. |

Le cœur n'atteint le processus, les boîtes de dialogue de fichiers et le fil d'interface qu'à travers des abstractions : il est testé sans fenêtre.
