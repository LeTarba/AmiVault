#!/usr/bin/env python3
"""
Test d'écriture complète d'un tag NTAG215 avec un ACR122 (pyscard).

Écrit trois motifs successifs sur toute la mémoire utilisateur (pages 4 à 129),
relit chaque page pour vérifier, puis remet tout à zéro :
  passe 1 : motif A (dépend du numéro de page)
  passe 2 : motif B (complément de A, pour tester les bits dans les deux sens)
  passe 3 : zéros (le tag est rendu vierge)

Ne touche JAMAIS : pages 0 à 3, verrous, configuration, mot de passe.
Refuse de s'exécuter sur un tag protégé par mot de passe.
Détruit le contenu de la mémoire utilisateur : à utiliser sur un tag de test.

Installation : pip install pyscard
Usage        : python nfc_writetest.py
"""
import argparse
import time
from smartcard.System import readers
from smartcard.scard import SCARD_RESET_CARD

PG_CFG0 = 0x83
FIRST_USER, LAST_USER = 4, 129
ZERO = [0x00, 0x00, 0x00, 0x00]
NON_INTERACTIVE = False
ASSUME_YES = False


def hexs(b):
    return " ".join(f"{x:02X}" for x in b) if b else "(vide)"


def tx(conn, apdu):
    data, sw1, sw2 = conn.transmit(apdu)
    return data, (sw1 << 8) | sw2


def thru(conn, payload):
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


def read16(conn, page):
    if conn is None:
        return None
    ok, st, d, brut = thru(conn, [0x30, page])
    return d[:16] if ok and len(d) >= 16 else None


def write_raw(conn, page, data):
    apdu = [0xFF, 0xD6, 0x00, page, 0x04] + list(data)
    try:
        d, sw = tx(conn, apdu)
        if sw == 0x9000:
            return True
    except Exception:
        pass
    ok, st, d, brut = thru(conn, [0xA2, page] + list(data))
    return ok


class Session:
    def __init__(self, conn, reader):
        self.conn, self.reader = conn, reader

    def recover(self):
        self.conn = reselect(self.conn, self.reader)
        return self.conn is not None


def write_page(sess, page, data):
    """Retourne le nombre d'essais nécessaires (1 = du premier coup),
    ou 0 en cas d'échec définitif."""
    for attempt in (1, 2, 3):
        if sess.conn is None:
            return 0
        write_raw(sess.conn, page, data)
        back = read16(sess.conn, page)
        if back is not None and back[:4] == list(data):
            return attempt
        if attempt < 3 and not sess.recover():
            return 0
    return 0


def read_user_pages(conn):
    pages = {}
    for p in range(FIRST_USER, LAST_USER + 1, 4):
        d = read16(conn, p)
        if d is None:
            return None
        for i in range(4):
            if p + i <= LAST_USER:
                pages[p + i] = d[4 * i:4 * i + 4]
    return pages


def motif_a(p):
    return [0xA5, p, 0x5A, p ^ 0xFF]


def motif_b(p):
    return [0x5A, p ^ 0xFF, 0xA5, p]


def motif_zero(p):
    return list(ZERO)


def run_pass(sess, nom, gen):
    print(f"\n  --- {nom} ---")
    t0 = time.time()
    retried, failed = [], []
    for p in range(FIRST_USER, LAST_USER + 1):
        r = write_page(sess, p, gen(p))
        if r == 0:
            failed.append(p)
        elif r > 1:
            retried.append((p, r))
        if (p - FIRST_USER + 1) % 30 == 0:
            print(f"    {p - FIRST_USER + 1}/{LAST_USER - FIRST_USER + 1} pages...")
        if sess.conn is None:
            print("    Connexion perdue : arrêt de la passe.")
            break
    dt = time.time() - t0

    if sess.conn is not None:
        sess.conn = reselect(sess.conn, sess.reader)
    pages = read_user_pages(sess.conn) if sess.conn else None
    mism = []
    if pages is None:
        print("    Relecture complète impossible.")
    else:
        mism = [p for p in sorted(pages) if pages[p] != gen(p)]

    total = LAST_USER - FIRST_USER + 1
    print(f"    Durée : {dt:.1f} s")
    print(f"    Écritures réussies du premier coup : {total - len(retried) - len(failed)}/{total}")
    if retried:
        print("    Pages qui ont demandé une nouvelle tentative : "
              + ", ".join(f"{p:02X}(x{r})" for p, r in retried[:20]))
    if failed:
        print("    Pages en échec : " + ", ".join(f"{p:02X}" for p in failed[:30]))
    if mism:
        print("    Pages différentes à la relecture complète : "
              + ", ".join(f"{p:02X}" for p in mism[:30]))
    ok = not failed and not mism and pages is not None
    print(f"    Résultat : {'OK' if ok else 'PROBLEME'}")
    return ok, retried, failed, mism


def test_tag(conn, reader):
    sess = Session(conn, reader)
    try:
        uid, sw = tx(conn, [0xFF, 0xCA, 0x00, 0x00, 0x00])
    except Exception as e:
        print(f"  Lecture UID impossible : {e}")
        return None
    print(f"  UID : {hexs(uid)}")

    cfg = read16(sess.conn, PG_CFG0)
    if cfg is None:
        print("  Config illisible : tag peut-être protégé. Abandon.")
        return None
    auth0, access = cfg[3], cfg[4]
    if auth0 <= 0x86 or access & 0x80:
        print(f"  Tag protégé (AUTH0={auth0:02X}, ACCESS={access:02X}). Abandon.")
        return None

    p0 = read16(sess.conn, 0)
    dyn = read16(sess.conn, 0x82)
    locks = (p0[10:12] if p0 else []) + (dyn[0:3] if dyn else [])
    if any(locks):
        print(f"  /!\\ Bits de verrouillage posés ({hexs(locks)}) : des pages peuvent refuser l'écriture.")

    pages = read_user_pages(sess.conn)
    if pages is None:
        print("  Lecture de la mémoire impossible. Abandon.")
        return None
    nz = [p for p in pages if pages[p] != ZERO]
    if nz:
        print(f"  Le tag contient {len(nz)} page(s) de données non nulles : elles seront détruites.")
    if not ask_yes_no("  Lancer le test d'écriture complet sur ce tag ? (o/N) "):
        return None

    r1 = run_pass(sess, "Passe 1 : motif A", motif_a)
    r2 = run_pass(sess, "Passe 2 : motif B (complément)", motif_b)
    r3 = run_pass(sess, "Passe 3 : remise à zéro", motif_zero)
    return r1, r2, r3


def conclusion(res):
    r1, r2, r3 = res
    echecs = set(r1[2]) | set(r2[2]) | set(r1[3]) | set(r2[3])
    reessais = len(r1[1]) + len(r2[1])
    print("\n  === Conclusion ===")
    if not echecs and reessais == 0:
        print("  Écriture complète parfaite, sans aucune nouvelle tentative.")
        print("  -> Le clone accepte toute la mémoire utilisateur via l'ACR122. "
              "L'échec de TagMo vient très probablement du téléphone (contact/pile NFC).")
    elif not echecs:
        print(f"  Écriture complète réussie, mais {reessais} page(s) ont demandé une nouvelle tentative.")
        print("  -> Puce capricieuse : elle perd parfois la communication. Une écriture longue "
              "depuis un téléphone qui bouge a de fortes chances d'échouer.")
    else:
        print("  Pages refusées : " + ", ".join(f"{p:02X}" for p in sorted(echecs)[:30]))
        print("  -> La puce n'accepte pas certaines zones : elle est inadaptée aux amiibo "
              "(ou des verrous sont posés).")
    if not r3[0]:
        print("  /!\\ La remise à zéro finale a échoué : relance nfc_reset.py sur ce tag.")


def main():
    global NON_INTERACTIVE, ASSUME_YES
    parser = argparse.ArgumentParser(description="Test d'ecriture NTAG215 via ACR122")
    parser.add_argument("--once", action="store_true",
                        help="traite un seul tag puis quitte")
    parser.add_argument("--no-prompt", action="store_true",
                        help="ne demande pas d'appuyer sur Entree avant detection")
    parser.add_argument("--yes", action="store_true",
                        help="repond oui aux confirmations destructives")
    args = parser.parse_args()
    NON_INTERACTIVE = args.no_prompt
    ASSUME_YES = args.yes

    rl = readers()
    if not rl:
        print("Aucun lecteur PC/SC détecté.")
        return
    reader = next((r for r in rl if "ACR122" in str(r).upper()), rl[0])
    print(f"Lecteur : {reader}")
    print("Ce test écrit sur toute la mémoire utilisateur. Utilise un tag de test.\n")

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
        res = test_tag(conn, reader)
        if res:
            conclusion(res)
        try:
            conn.disconnect()
        except Exception:
            pass
        if args.once:
            break
        if not args.no_prompt:
            input("\nRetire le tag (Entrée pour continuer)... ")


if __name__ == "__main__":
    main()
