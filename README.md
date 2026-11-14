# Paris Guessr

Un GeoGuessr de Paris en 3D. À chaque manche, tu atterris en vue à la première personne dans une rue tirée au hasard dans Paris. Tu te balades, puis tu ouvres la carte et tu poses ton repère là où tu penses être.

▶ **Jouer dans le navigateur (ordinateur et mobile) :** https://doriang-epitech.github.io/ParisGuessr/

Il existe aussi une **version Three.js**, sans Unity, plus légère (environ 2,5 Mo au démarrage) : https://doriang-epitech.github.io/ParisGuessr3JS/ ([code](https://github.com/DorianG-Epitech/ParisGuessr3JS))

Il n'y a **aucune texture** : la ville est reconstruite uniquement à partir des **formes 3D réelles de l'IGN** (bâtiments, hauteurs, toits, relief, chaussées), avec un rendu à contours noirs façon maquette d'architecte.

## Règles

- 5 manches par partie, chacune dans un arrondissement différent.
- Score par manche : `5000 × e^(−distance / 1200 m)`. À moins de 25 m, tu marques les 5 000 points. À 500 m, environ 3 300 points. À 2 km, environ 950 points.
- À la fin d'une manche, la carte affiche ta position réelle, la rue et l'arrondissement. À la fin de la partie, tu as un récapitulatif et le score total sur 25 000.

## Commandes

**Ordinateur :** clic pour contrôler la vue · ZQSD/WASD ou les flèches pour marcher · Maj pour courir · **M** ou Espace pour la carte · **R** pour revenir au départ · Entrée pour valider. Sur la carte, la molette zoome, on glisse pour se déplacer et on clique pour poser le repère.

**Mobile (en paysage) :** glisse le pouce à gauche (joystick flottant) pour marcher, à droite pour regarder. Le bouton **CARTE** ouvre la carte : touche pour poser le repère, pince ou utilise +/- pour zoomer, glisse pour te déplacer, puis **Valider**.

## Quelle partie de Paris ?

**Tout Paris.** Les points de départ (6 000 points tirés à l'avance) sont répartis sur toutes les rues de la commune, au prorata de la longueur des voies. On exclut les tunnels, les ponts, le périphérique et les bretelles, ainsi que les points sans bâtiment autour (bois, grands espaces vides).

Pour que ça tienne dans un navigateur, la géométrie n'est **pas** stockée sous forme de maillages. On stocke seulement les emprises des bâtiments, leurs hauteurs, les axes des rues, les bordures, les arbres et une grille de relief à 5 m. Le jeu extrude tout ça en 3D au moment du chargement. Paris (avec une marge sur la petite couronne) est découpé en **3 073 tuiles de 250 m** d'environ 20 Ko chacune, 63 Mo au total. Seules les tuiles autour du joueur sont téléchargées, soit environ 0,5 à 1 Mo par manche, et elles se chargent au fil des déplacements.

- Premier téléchargement : environ **18 Mo** (moteur et carte de Paris).
- Vue limitée par le brouillard à environ 330 m (270 m sur mobile).

## Données et méthode

- **Bâtiments** : IGN BD TOPO® V3. On prend l'emprise et `hauteur` au-dessus de `altitude_minimale_sol` pour faire un prisme à toit plat. On ajoute un étage en retrait façon mansarde à partir de `altitude_maximale_toit` quand elle est connue. Sinon, on ajoute 2,8 m sur les immeubles résidentiels de 4 étages ou plus : c'est une approximation. Des traits fins marquent les étages tous les 3 m.
- **Relief** : IGN RGE ALTI® (grille de 4 m rééchantillonnée à 5 m).
- **Rues** : IGN BD TOPO® tronçons de route. On utilise la largeur de chaussée, les ponts avec leur altitude propre et le nom de la voie. Les bordures sont le contour de l'union des chaussées.
- **Eau, espaces verts, cimetières** : IGN BD TOPO®. Ils colorent le sol.
- **Arbres** : Ville de Paris, jeu de données « Les arbres » (environ 200 000 arbres et leur hauteur).
- **Carte de devinette** : rendue hors ligne depuis la BD TOPO (bâtiments, voies par importance, Seine, parcs, voies ferrées, limites d'arrondissements).

Pipeline (`Tools/`) :

1. `python3 Tools/fetch_ign.py` télécharge BD TOPO (WFS) et RGE ALTI (WMS) depuis la Géoplateforme IGN, sans clé d'API.
2. `bash Tools/fetch_trees.sh` récupère les arbres de opendata.paris.fr.
3. `python3 Tools/build_tiles.py` produit `Assets/StreamingAssets/tiles/*.bin`, `Resources/spawns.bytes` et `tiles.bytes`.
4. `python3 Tools/make_map.py` produit `Resources/paris_map.png`.
5. Unity : `-executeMethod PGSetup.BuildScene`, puis `PGSetup.BuildWeb` (build WebGL dans `Builds/WebGL`, copié dans `docs/`).

## Projet Unity

- Unity **6000.3.28f1**, pipeline **Built-in**, ancien Input Manager. Le rendu utilise trois shaders maison, compatibles WebGL 2 et mobiles :
  - `PG/Faces` : ombrage plat et brouillard ;
  - `PG/Lines` : contours d'épaisseur constante à l'écran, découpés au plan proche ;
  - `PG/Hull` : contour par coque inversée pour les arbres.
- Scripts principaux (`Assets/ParisGuessr/Scripts`) :
  - `TileBuilder.cs` : extrusion des bâtiments, rubans de rue, bordures, arbres, collisions ;
  - `TileStreamer.cs` : chargement et déchargement des tuiles autour du joueur ;
  - `PGPlayer.cs` : déplacement FPS au clavier, à la souris et au tactile ;
  - `PGGame.cs` : manches, carte, score et interface ;
  - `PGWorld.cs` : métadonnées, points de départ, noms de rues.
- Ouvrir le projet dans Unity Hub, puis la scène `Assets/Scenes/Paris.unity`, et Play.

## Limites connues

- Toits plats, à part l'étage en retrait. Pas de fenêtres, de façades détaillées ni de mobilier urbain. Les monuments sans « bâtiment » dans la BD TOPO (tour Eiffel par exemple) n'apparaissent pas.
- Les trottoirs ne sont pas séparés de la chaussée en hauteur. Le relief à 5 m arrondit les quais.
- Les noms de rues viennent de la BD TOPO, en majuscules et sans accents, puis sont remis en forme automatiquement.

## Crédits et licences

- **IGN — Géoplateforme** : BD TOPO® V3 et RGE ALTI®, **Licence Ouverte / Open Licence Etalab 2.0**. Source : IGN, https://geoservices.ign.fr
- **Ville de Paris** : « Les arbres », https://opendata.paris.fr/explore/dataset/les-arbres/, licence **ODbL 1.0**. Base de données dérivée (positions et hauteurs des arbres dans les tuiles) sous la même licence.
- Aucune donnée OpenStreetMap n'est utilisée dans cette version.
