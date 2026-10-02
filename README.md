# AmiVault

Application WinForms pour lire, diagnostiquer, tester, réinitialiser et écrire des tags `NTAG215` avec un lecteur `ACS ACR122`, avec prise en charge de dumps amiibo chiffrés.

## Fonctionnalités

- Détection du lecteur `PC/SC` et lecture automatique des tags.
- Diagnostic `NTAG215` détaillé : `UID`, `GET_VERSION`, `READ_SIG`, pages mémoire, configuration, protection, verdict lisible.
- Bibliothèque amiibo locale avec scan récursif des fichiers `.bin`.
- Enrichissement des métadonnées via `amiiboapi.org`.
- Écriture amiibo avec adaptation à l'UID du tag et vérification post-écriture.
- Mode `dry run` non destructif.
- Outils Python intégrés pour `dump`, `reset`, `write test` et diagnostic bas niveau.
- Interface multilingue : Français, Anglais, Espagnol, Allemand.

## Arborescence

```text
AmiVault/
  Acr122NfcReader/
    Acr122NfcReader.csproj
    Program.cs
    Form1.cs
    Form1.Designer.cs
    AmiiboDumpPreparer.cs
  nfc_diag.py
  nfc_dump.py
  nfc_reset.py
  nfc_writetest.py
  requirements.txt
  lancer_acr122_reader.bat
  compiler_et_lancer_acr122_reader.bat
  publier_release_acr122_reader.bat
  publier_standalone_win_x64.bat
```

## Dépendances

### Application C#

- Windows
- `.NET 8 SDK` pour compiler depuis les sources
- Packages NuGet :
  - `PCSC` `7.0.1`
  - `PCSC.Iso7816` `7.0.1`

### Scripts Python

- `Python 3`
- `pyscard`

Installation :

```bash
pip install -r requirements.txt
```

### Matériel / système

- Lecteur `ACS ACR122`
- Service `PC/SC` actif sur Windows
- Tags de test `NTAG215`

## Fichiers non inclus volontairement

Ce dépôt source ne contient pas :

- les clés amiibo privées :
  - `key_retail.bin`
  - `unfixed-info.bin`
  - `locked-secret.bin`
- les bibliothèques de dumps amiibo `.bin`
- les dossiers `bin/`, `obj/`, `dist/`
- les dumps générés localement
- les fichiers d'état utilisateur

Tu dois fournir localement tes propres clés et tes propres fichiers `.bin`.

## Compilation

Depuis la racine du dépôt :

```bash
dotnet build .\Acr122NfcReader\Acr122NfcReader.csproj -c Debug
```

Ou via le script batch :

```bat
compiler_et_lancer_acr122_reader.bat
```

## Lancement

```bat
lancer_acr122_reader.bat
```

## Publication Release

Publication framework-dependent classique :

```bat
publier_release_acr122_reader.bat
```

## Publication standalone

Pour générer une version Windows `x64` autonome qui n'a pas besoin d'une installation .NET sur la machine cible :

```bat
publier_standalone_win_x64.bat
```

Sortie attendue :

```text
dist\AmiVault-win-x64-selfcontained
```

## Clés amiibo

L'écriture amiibo nécessite l'un des formats suivants dans un dossier local que tu sélectionnes ensuite depuis `Paramètres` :

- `key_retail.bin` de `160` octets
- ou le couple :
  - `unfixed-info.bin`
  - `locked-secret.bin`

Ne publie jamais ces fichiers sur GitHub.

## Notes de sécurité

- Les verrous statiques et dynamiques sont irréversibles.
- N'utilise jamais les scripts de reset/écriture sur un amiibo original.
- Vérifie toujours les opérations sur des tags de test.
- Le mode `dry run` n'écrit rien sur le tag.

## API externe

- Documentation amiibo API : [https://amiiboapi.org/docs/](https://amiiboapi.org/docs/)

