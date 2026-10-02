#!/usr/bin/env python3
"""
Remise à zéro de tags NTAG215 avec un lecteur ACR122 (pyscard).

Ce que le script fait :
  - efface la mémoire utilisateur (pages 4 à 129) ;
  - tente de remettre la page 3 (Capability Container) à E1 10 3E 00 ;
  - remet la configuration d'usine (AUTH0=FF, ACCESS=00 05 00 00,
    mot de passe FF FF FF FF, PACK 00 00) si elle a été modifiée ;
  - vérifie tout par relecture.

Ce qu'il ne peut PAS faire (verrous matériels, irréversibles) :
  - annuler les bits de verrouillage (page 2 et page 0x82) ;
  - rétablir des bits OTP déjà à 1 (page 3) sur une vraie puce NXP ;
  - déverrouiller une configuration verrouillée (CFG_LOCK).

ATTENTION : l'effacement détruit définitivement les données du tag.
Ne l'utilise jamais sur une figurine ou une carte amiibo d'origine.

Installation : pip install pyscard
Usage        : python nfc_reset.py
"""
import argparse
import time
from smartcard.System import readers
from smartcard.scard import SCARD_RESET_CARD

VERSION_NTAG215 = [0x00, 0x04, 0x04, 0x02, 0x01, 0x00, 0x11, 0x03]
CC_BLANK = [0xE1, 0x10, 0x3E, 0x00]
CFG0_DEF = [0x04, 0x00, 0x00, 0xFF]
CFG1_DEF = [0x00, 0x05, 0x00, 0x00]
PWD_DEF = [0xFF, 0xFF, 0xFF, 0xFF]
PACK_DEF = [0x00, 0x00, 0x00, 0x00]
ZERO = [0x00, 0x00, 0x00, 0x00]
PG_DYN_LOCK, PG_CFG0, PG_CFG1, PG_PWD, PG_PACK = 0x82, 0x83, 0x84, 0x85, 0x86
FIRST_USER, LAST_USER = 4, 129
NON_INTERACTIVE = False
ASSUME_YES = False
TRY_KNOWN_AMIIBO_PWD = False


def hexs(b):
    return " ".join(f"{x:02X}" for x in b) if b else "(vide)"


def tx(conn, apdu):
    data, sw1, sw2 = conn.transmit(apdu)
    return data, (sw1 << 8) | sw2


def thru(conn, payload):
    """Commande brute vers le tag. Retourne (ok, statut, donnees, brut)."""
    cmd = [0xD4, 0x42] + payload
    apdu = [0xFF, 0x00, 0x00, 0x00, len(cmd)] + cmd
    try:
        data, sw = tx(conn, apdu)
    except Exception as e:
        return False, None, [], f"exception: {e}"
    brut = f"{hexs(data)} | SW={sw:04X}"
    if sw != 0x9000 or len(data) < 3 or data[0] != 0xD5 or data[1] != 0x43:
        return False, None, [], brut
    return data[2] == 0x00, data[2], list(data[3:]), brut


def amiibo_password(uid):
    return [
        uid[1] ^ uid[3] ^ 0xAA,
        uid[2] ^ uid[4] ^ 0x55,
        uid[3] ^ uid[5] ^ 0xAA,
        uid[4] ^ uid[6] ^ 0x55,
    ]


def connect_retry(reader, timeout=60):
    t0 = time.time()
    while time.time() - t0 < timeout:
        try:
            conn = reader.createConnection()
            conn.connect()
            return conn
        except Exception:
            time.sleep(0.4)
    return None


def reselect(conn, reader):
    try:
        conn.reconnect(disposition=SCARD_RESET_CARD)
        return conn
    except Exception:
        pass
    try:
        conn.disconnect()
    except Exception:
        pass
    new = connect_retry(reader, timeout=3)
    if new is None:
        if NON_INTERACTIVE:
            return None
        input("  Resélection impossible : retire le tag, repose-le, puis Entrée... ")
        new = connect_retry(reader, timeout=30)
    return new


def ask_yes_no(prompt, default=False):
    if ASSUME_YES:
        print(f"{prompt} o (auto)")
        return True
    rep = input(prompt).strip().lower()
    if not rep:
        return default
    return rep == "o"


class Tag:
    def __init__(self, conn, reader, uid):
        self.conn, self.reader, self.uid = conn, reader, uid
        self.pwd = None  # mot de passe qui a fonctionné (None = pas d'authentification)

    def recover(self):
        """Resélectionne le tag et se ré-authentifie si besoin."""
        self.conn = reselect(self.conn, self.reader)
        if self.conn is None:
            return False
        if self.pwd is not None:
            ok, st, d, brut = thru(self.conn, [0x1B] + self.pwd)
            return ok
        return True


def read16(conn, page):
    if conn is None:
        return None
    ok, st, d, brut = thru(conn, [0x30, page])
    return d[:16] if ok and len(d) >= 16 else None


def write_raw(tag, page, data):
    apdu = [0xFF, 0xD6, 0x00, page, 0x04] + list(data)
    try:
        d, sw = tx(tag.conn, apdu)
        if sw == 0x9000:
            return True
    except Exception:
        pass
    ok, st, d, brut = thru(tag.conn, [0xA2, page] + list(data))
    return ok


def write_page(tag, page, data):
    """Écrit une page puis la relit. Retourne True si la relecture correspond."""
    for attempt in range(2):
        if tag.conn is None:
            return False
        write_raw(tag, page, data)
        back = read16(tag.conn, page)
        if back is not None and back[:4] == list(data):
            return True
        if attempt == 0 and not tag.recover():
            return False
    return False


def read_user_pages(conn):
    pages = {}
    for p in range(FIRST_USER, LAST_USER + 1, 4):
        d = read16(conn, p)
        if d is None:
            return None, p
        for i in range(4):
            if p + i <= LAST_USER:
                pages[p + i] = d[4 * i:4 * i + 4]
    return pages, None


def try_auth(tag):
    candidates = [("usine FF FF FF FF", PWD_DEF)]
    if len(tag.uid) == 7:
        candidates.append((f"type amiibo {hexs(amiibo_password(tag.uid))}",
                           amiibo_password(tag.uid)))
    for i, (label, pwd) in enumerate(candidates):
        if i > 0:
            tag.conn = reselect(tag.conn, tag.reader)
            if tag.conn is None:
                return False
        ok, st, d, brut = thru(tag.conn, [0x1B] + pwd)
        print(f"  PWD_AUTH {label} : {'OK' if ok else 'ECHEC'} -> {brut}")
        if ok:
            tag.pwd = pwd
            return True
    return False


def try_known_amiibo_auth_once(tag):
    if len(tag.uid) != 7:
        print("  Impossible de calculer le mot de passe amiibo : UID inattendu.")
        return False
    pwd = amiibo_password(tag.uid)
    print("  AUTHLIM non nul : une seule tentative sera faite avec le mot de passe amiibo "
          f"connu {hexs(pwd)}.")
    ok, st, d, brut = thru(tag.conn, [0x1B] + pwd)
    print(f"  PWD_AUTH type amiibo {hexs(pwd)} : {'OK' if ok else 'ECHEC'} -> {brut}")
    if ok:
        tag.pwd = pwd
        return True
    return False


def auth_with_pwd(tag, pwd, label, expected_pack=None):
    if tag.conn is None:
        return False
    ok, st, d, brut = thru(tag.conn, [0x1B] + pwd)
    print(f"  PWD_AUTH {label} : {'OK' if ok else 'ECHEC'} -> {brut}")
    if not ok:
        return False
    if expected_pack is not None:
        if len(d) < 2 or d[0] != expected_pack[0] or d[1] != expected_pack[1]:
            print(f"    PACK inattendu : attendu {hexs(expected_pack[:2])}, reçu {hexs(d[:2])}.")
            return False
    tag.pwd = pwd
    return True


def write_default_password_and_pack(tag):
    if tag.conn is None:
        return False

    for attempt in range(2):
        if not write_raw(tag, PG_PWD, PWD_DEF):
            if attempt == 0 and tag.recover():
                continue
            return False

        tag.conn = reselect(tag.conn, tag.reader)
        if tag.conn is None:
            return False

        if not auth_with_pwd(tag, PWD_DEF, "usine FF FF FF FF après écriture PWD"):
            if attempt == 0 and tag.recover():
                continue
            return False

        if not write_raw(tag, PG_PACK, PACK_DEF):
            if attempt == 0 and tag.recover():
                continue
            return False

        tag.conn = reselect(tag.conn, tag.reader)
        if tag.conn is None:
            return False

        if auth_with_pwd(tag, PWD_DEF, "usine FF FF FF FF après écriture PACK", PACK_DEF):
            print("  Mot de passe et PACK remis à FF FF FF FF / 00 00.")
            return True

        if attempt == 0 and tag.recover():
            continue

    return False


def reset_tag(conn, reader):
    try:
        uid, sw = tx(conn, [0xFF, 0xCA, 0x00, 0x00, 0x00])
    except Exception as e:
        print(f"  Lecture UID impossible : {e}")
        return False
    tag = Tag(conn, reader, uid)
    print(f"  UID : {hexs(uid)}")

    # Type de tag
    ok, st, d, brut = thru(tag.conn, [0x60])
    if not (ok and d == VERSION_NTAG215):
        print(f"  GET_VERSION atypique ({brut}).")
        if not ask_yes_no("  Ce n'est peut-être pas un NTAG215. Continuer quand même ? (o/N) "):
            return False

    # Configuration et protection
    cfg = read16(tag.conn, PG_CFG0)
    protected = True
    authlim = None
    cfg_lock = False
    if cfg is None:
        print("  Pages de config illisibles : tag probablement protégé en lecture.")
    else:
        auth0, access = cfg[3], cfg[4]
        authlim = access & 0x07
        cfg_lock = bool(access & 0x40)
        protected = auth0 <= PG_PACK or bool(access & 0x80)
        print(f"  AUTH0={auth0:02X}  ACCESS={access:02X}  (PROT={int(bool(access & 0x80))}, "
              f"AUTHLIM={authlim}, CFG_LOCK={int(cfg_lock)})")
        if cfg_lock:
            print("  /!\\ Configuration verrouillée (CFG_LOCK) : elle ne pourra pas être remise à zéro.")
            print("  /!\\ La protection par mot de passe ne pourra pas être totalement supprimée si AUTH0 reste à 04.")

    if protected:
        if authlim not in (0, None):
            if not TRY_KNOWN_AMIIBO_PWD:
                print(f"  Tag protégé avec AUTHLIM={authlim} : les essais ratés sont comptés, "
                      "je n'essaie pas de mot de passe pour ne pas bloquer le tag.")
                return False
            print(f"  Tag protégé avec AUTHLIM={authlim}.")
            if not try_known_amiibo_auth_once(tag):
                print("  Authentification impossible avec le mot de passe amiibo connu.")
                return False
        else:
            print("  Tag protégé : tentative d'authentification.")
            if not try_auth(tag):
                print("  Authentification impossible : mot de passe inconnu, tag non effaçable.")
                return False

    # État des verrous
    p0 = read16(tag.conn, 0)
    dyn = read16(tag.conn, PG_DYN_LOCK)
    static_lock = p0[10:12] if p0 else None
    dyn_lock = dyn[0:3] if dyn else None
    print(f"  Verrous statiques : {hexs(static_lock) if static_lock else '?'}   "
          f"dynamiques : {hexs(dyn_lock) if dyn_lock else '?'}")
    if (static_lock and any(static_lock)) or (dyn_lock and any(dyn_lock)):
        print("  /!\\ Des bits de verrouillage sont posés (irréversibles) : les pages "
              "concernées ne pourront pas être effacées.")

    # Contenu actuel
    pages, bad = read_user_pages(tag.conn)
    if pages is None:
        print(f"  Lecture impossible à la page {bad:02X} : arrêt.")
        return False
    todo = [p for p in sorted(pages) if pages[p] != ZERO]
    print(f"  Mémoire utilisateur : {len(todo)} page(s) non nulle(s) sur {len(pages)}.")
    cc = p0[12:16] if p0 else None
    print(f"  Page 3 (CC) : {hexs(cc) if cc else '?'}")

    if not ask_yes_no("  EFFACER ce tag (irréversible) ? (o/N) "):
        print("  Ignoré.")
        return False

    # 1. Mémoire utilisateur
    failed = []
    for n, p in enumerate(todo, 1):
        if not write_page(tag, p, ZERO):
            failed.append(p)
        if n % 20 == 0:
            print(f"    {n}/{len(todo)} pages...")
        if tag.conn is None:
            print("  Connexion perdue.")
            return False
    print(f"  Mémoire utilisateur : {len(todo) - len(failed)}/{len(todo)} pages remises à zéro.")
    if failed:
        print("    Échecs : " + ", ".join(f"{p:02X}" for p in failed)
              + "  (pages probablement verrouillées)")

    # 2. Page 3 (OTP sur une vraie puce)
    if cc is not None and cc != CC_BLANK:
        if write_page(tag, 3, CC_BLANK):
            print("  Page 3 remise à E1 10 3E 00.")
        else:
            now = read16(tag.conn, 0)
            print(f"  Page 3 non modifiable (zone OTP) : reste {hexs(now[12:16]) if now else '?'}. "
                  "Sans conséquence pour écrire un amiibo.")
            if tag.conn is None:
                return False

    # 3. Configuration d'usine
    for page, default, label in ((PG_CFG0, CFG0_DEF, "CFG0"), (PG_CFG1, CFG1_DEF, "CFG1")):
        cur = read16(tag.conn, page)
        if cur is not None and cur[:4] != default:
            ok = write_page(tag, page, default)
            print(f"  {label} {hexs(cur[:4])} -> {hexs(default)} : {'OK' if ok else 'ECHEC'}")
    pwd_default_ok = False
    if tag.pwd is not None and tag.pwd != PWD_DEF:
        pwd_default_ok = write_default_password_and_pack(tag)
        if not pwd_default_ok:
            print("  /!\\ Impossible de confirmer le retour à FF FF FF FF / 00 00.")
    else:
        pwd_default_ok = True

    # 4. Vérification finale
    if tag.conn is not None:
        tag.conn = reselect(tag.conn, tag.reader)
    cfg2 = read16(tag.conn, PG_CFG0) if tag.conn else None
    final_auth0 = cfg2[3] if cfg2 else 0xFF
    final_access = cfg2[4] if cfg2 else 0x00
    final_protected = final_auth0 <= PG_PACK or bool(final_access & 0x80)
    if tag.conn is not None and final_protected:
        pwd_default_ok = auth_with_pwd(tag, PWD_DEF, "usine FF FF FF FF (vérification finale)") and pwd_default_ok
    final, bad = read_user_pages(tag.conn) if tag.conn else (None, None)
    if final is None:
        print("  Vérification impossible.")
        return False
    left = [p for p in sorted(final) if final[p] != ZERO]
    cfg_ok = cfg2 is not None and cfg2[:8] == CFG0_DEF + CFG1_DEF
    if final_protected and not cfg_ok:
        print("  /!\\ La protection reste active car la configuration n'a pas pu revenir à l'état usine.")
    print(f"  Vérification : {len(left)} page(s) non nulle(s), "
          f"config d'usine {'OK' if cfg_ok else 'différente'}, "
          f"PWD_AUTH usine {'OK' if pwd_default_ok else 'ECHEC'}.")
    if left:
        print("    Pages restantes : " + ", ".join(f"{p:02X}" for p in left[:20]))
    return not left and pwd_default_ok and (cfg_ok or cfg_lock)


def main():
    global NON_INTERACTIVE, ASSUME_YES
    parser = argparse.ArgumentParser(description="Remise a zero NTAG215 via ACR122")
    parser.add_argument("--once", action="store_true",
                        help="traite un seul tag puis quitte")
    parser.add_argument("--no-prompt", action="store_true",
                        help="ne demande pas d'appuyer sur Entree avant detection")
    parser.add_argument("--yes", action="store_true",
                        help="repond oui aux confirmations destructives")
    parser.add_argument("--try-known-amiibo-pwd", action="store_true",
                        help="si AUTHLIM est non nul, tente une seule authentification "
                             "avec le mot de passe amiibo calcule depuis l'UID")
    args = parser.parse_args()
    NON_INTERACTIVE = args.no_prompt
    ASSUME_YES = args.yes
    global TRY_KNOWN_AMIIBO_PWD
    TRY_KNOWN_AMIIBO_PWD = args.try_known_amiibo_pwd

    rl = readers()
    if not rl:
        print("Aucun lecteur PC/SC détecté.")
        return
    reader = next((r for r in rl if "ACR122" in str(r).upper()), rl[0])
    print(f"Lecteur : {reader}")
    print("Rappel : l'effacement est définitif. Pas sur un amiibo d'origine.\n")

    resultats = []
    n = 0
    while True:
        if not args.no_prompt:
            rep = input("Pose un tag sur le lecteur, puis Entrée (q pour finir) : ").strip().lower()
            if rep == "q":
                break
        n += 1
        print(f"\n=== Tag #{n} ===")
        conn = connect_retry(reader)
        if conn is None:
            print("  Aucun tag détecté.\n")
            continue
        ok = reset_tag(conn, reader)
        resultats.append((n, ok))
        print(f"  >>> {'Tag remis à zéro.' if ok else 'Tag non (ou partiellement) réinitialisé.'}\n")
        try:
            conn.disconnect()
        except Exception:
            pass
        if args.once:
            break
        if not args.no_prompt:
            input("Retire le tag (Entrée pour continuer)... ")

    if resultats:
        print("\n=== Récapitulatif ===")
        for n, ok in resultats:
            print(f"#{n} : {'OK' if ok else 'à vérifier'}")


if __name__ == "__main__":
    main()
