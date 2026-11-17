> **Language / Langue**: [English](dashboard.md) · **Français**

# Référence du tableau de bord

`PortAtlas audit` affiche un tableau de bord plein écran dans le terminal, sauf avec `--no-tui`, avec `EnableDashboard` à `false`, ou quand l'entrée ou la sortie du processus est redirigée.

## Vues

| Vue | Contenu |
| :--- | :--- |
| Audit Log | Flux dédupliqués enregistrés depuis le démarrage, y compris ceux rechargés depuis le journal CSV |
| Active Sockets | Sockets vus par le dernier balayage, avant application des filtres d'enregistrement |

## Touches

| Touche | Action |
| :--- | :--- |
| `Q` | Arrêter l'audit et quitter |
| `C` | Redessiner l'écran |
| `V` ou `F` | Basculer entre les deux vues |
| `M` | Écrire la carte HTML (`OutputHtmlMapPath`, ou `portatlas_topology.html`), et le diagramme Draw.io quand `OutputDrawioPath` est renseigné |
| `↑` `↓` `←` `→` | Déplacer la sélection dans le tableau |
| `N` ou `Page Down` | Page suivante |
| `P` ou `Page Up` | Page précédente |
| `L` ou `+` | Charger 100 lignes de plus |
| `A` | Charger toutes les lignes |
| `Enter`, `X` ou `E` | Exclure la valeur de la cellule sélectionnée |
| `Esc` | Annuler la sélection |

`Ctrl+C` et `SIGTERM` arrêtent l'audit comme `Q`.

## Exclure depuis le tableau de bord

Exclure une cellule ajoute sa valeur au fichier de configuration donné par `--config` (ou celui par défaut) et l'applique aussitôt :

| Colonne sélectionnée | Clé mise à jour |
| :--- | :--- |
| Port local | `ExcludedLocalPorts` |
| Adresse distante | `ExcludedRemoteIps` |
| Port distant | `ExcludedRemotePorts` |
| Processus | `ExcludedExactNames` |
| Cible | `ExcludedExactNames` |

La nouvelle version est d'abord écrite à côté du fichier, sous son nom suivi de `.tmp` puis de `.pending`, et ne le remplace qu'ensuite : un programme qui lit le fichier obtient l'ancienne version ou la nouvelle, jamais un mélange des deux, et une interruption laisse l'ancien fichier intact. Windows refuse le remplacement tant qu'un autre programme tient le fichier ouvert ; l'enregistrement compte alors comme non fait, et le fichier `.pending` reste jusqu'à ce que l'enregistrement suivant le remplace. Si le fichier lui-même manque alors qu'un fichier `.pending` est présent, l'enregistrement suivant part de la version `.pending`.

Quand le fichier ne peut pas être écrit (en cours d'utilisation, en lecture seule, accès refusé) ou ne contient plus une configuration valide, il est laissé tel quel et le bandeau se termine par `not saved to …` et la cause. L'exclusion s'applique quand même jusqu'à l'arrêt de l'audit, et la prochaine exclusion qui peut être enregistrée écrit aussi les précédentes.

## Masquer pour des captures d'écran

`--mask-server`, `--mask-ip` et `--obfuscate` ne changent que l'affichage du tableau de bord ; le journal d'audit garde les vraies valeurs.
