#!/bin/bash
# Arbres d'alignement et des espaces verts de Paris — Ville de Paris, opendata.paris.fr (ODbL)
cd "${PG_DATA:-/workspace/parisguessr/data}"
curl -sS -m 600 -A "ParisGuessr/1.0" "https://opendata.paris.fr/api/explore/v2.1/catalog/datasets/les-arbres/exports/csv?select=geo_point_2d,hauteurenm,circonferenceencm&delimiter=%3B" -o arbres_paris.csv
wc -l arbres_paris.csv
