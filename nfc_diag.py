#!/usr/bin/env python3
"""
Diagnostic NTAG215 pour lecteur ACR122 (pyscard).
Aucune écriture : lecture, GET_VERSION, READ_SIG, pages de config, PWD_AUTH.

Installation : pip install pyscard
Usage        : python nfc_diag.py
"""
import time
from smartcard.System import readers
from smartcard.scard import SCARD_RESET_CARD

VERSION_NTAG215 = [0x00, 0x04, 0x04, 0x02, 0x01, 0x00, 0x11, 0x03]


def hexs(b):
    return " ".join(f"{x:02X}" for x in b) if b else "(vide)"


def tx(conn, apdu):
    data, sw1, sw2 = conn.transmit(apdu)
    return data, (sw1 << 8) | sw2


def thru(conn, payload):
    """Commande brute vers le tag (PN532 InCommunicateThru).
    Retourne (ok, statut_pn532, donnees, brut_hex)."""
    cmd = [0xD4, 0x42] + payload
    apdu = [0xFF, 0x00, 0x00, 0x00, len(cmd)] + cmd
    try:
        data, sw = tx(conn, apdu)
    except Exception as e:
        return False, None, [], f"exception: {e}"
    brut = f"{hexs(data)} | SW={sw:04X}"
    if sw != 0x9000:
        return False, None, [], brut
    if len(data) < 3 or data[0] != 0xD5 or data[1] != 0x43:
        return False, None, [], brut
    statut = data[2]
    return statut == 0x00, statut, list(data[3:]), brut


def amiibo_password(uid):
    """Mot de passe type amiibo (TagMo) calculé depuis l'UID de 7 octets."""
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
    """Resélectionne le tag (nécessaire après un PWD_AUTH raté)."""
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
        input("  Resélection impossible : retire le tag, repose-le, puis Entrée... ")
        new = connect_retry(reader, timeout=30)
    return new


def run_tag(conn, reader):
    res = {}

    # 1. UID
    try:
        uid, sw = tx(conn, [0xFF, 0xCA, 0x00, 0x00, 0x00])
    except Exception as e:
        print(f"  Lecture UID impossible : {e}")
        return res
    print(f"  UID : {hexs(uid)} (SW={sw:04X})")
    res["uid"] = uid

    # 2. Lecture de contrôle page 0
    ok, st, d, brut = thru(conn, [0x30, 0x00])
    print(f"  READ page 0 : {'OK' if ok else 'ECHEC'} -> {brut}")
    res["read0"] = ok
    page0 = d[:16] if ok else []
    if not ok:
        print("  -> Si cette lecture échoue, le problème vient de la chaîne de commandes, pas du tag.")

    # 3. GET_VERSION
    ok, st, d, brut = thru(conn, [0x60])
    print(f"  GET_VERSION : {'OK' if ok else 'ECHEC'} -> {brut}")
    res["version_ok"] = ok and d == VERSION_NTAG215
    if ok:
        print("    " + ("= NTAG215 NXP authentique (00 04 04 02 01 00 11 03)"
                        if res["version_ok"] else "= réponse inhabituelle pour un NTAG215"))
    else:
        print("    = pas de réponse GET_VERSION (fréquent sur les clones)")

    # 4. READ_SIG
    ok, st, d, brut = thru(conn, [0x3C, 0x00])
    print(f"  READ_SIG : {'OK' if ok else 'ECHEC'} -> {brut}")
    sig_ok = False
    if not ok:
        print("    = commande non supportée : typique d'un clone")
    elif len(d) != 32:
        print(f"    = longueur inattendue ({len(d)} octets au lieu de 32) : clone probable")
    elif len(set(d)) <= 1:
        print("    = signature vide ou constante (tout 00 ou tout FF) : clone")
    elif page0 and (d[:16] == page0 or d[16:] == page0):
        print("    = la réponse recopie la mémoire (pages 0 à 3) au lieu d'une signature : clone")
    elif d[:16] == d[16:]:
        print("    = signature composée de deux moitiés identiques : clone")
    else:
        sig_ok = True
        print("    = signature plausible (non nulle, différente de la mémoire). "
              "Seule la vérification ECC avec la clé publique NXP le confirme.")
    res["sig_ok"] = sig_ok

    # 5. Pages de config (83 à 86) : CFG0, CFG1, PWD, PACK
    ok, st, d, brut = thru(conn, [0x30, 0x83])
    authlim = None
    if ok and len(d) >= 8:
        auth0 = d[3]
        access = d[4]
        authlim = access & 0x07
        prot = bool(access & 0x80)
        print(f"  CFG0={hexs(d[0:4])}  CFG1={hexs(d[4:8])}")
        print(f"    AUTH0={auth0:02X}  PROT={int(prot)}  AUTHLIM={authlim}")
    else:
        print(f"  Lecture config : ECHEC -> {brut}")
    res["authlim"] = authlim

    # 6. PWD_AUTH (seulement si AUTHLIM = 0, sinon risque de bloquer le tag)
    res["auth_default"] = None
    res["auth_amiibo"] = None
    if authlim not in (0, None):
        print(f"  PWD_AUTH ignoré : AUTHLIM={authlim} (les essais ratés sont comptés).")
        return res
    if authlim is None:
        rep = input("  AUTHLIM inconnu. Tenter PWD_AUTH quand même ? (o/N) ").strip().lower()
        if rep != "o":
            return res

    tests = [
        ("auth_default", "FF FF FF FF (usine)", [0xFF, 0xFF, 0xFF, 0xFF]),
    ]
    if len(uid) == 7:
        tests.append(("auth_amiibo", f"{hexs(amiibo_password(uid))} (type amiibo)",
                      amiibo_password(uid)))

    for key, label, pwd in tests:
        conn = reselect(conn, reader)
        if conn is None:
            break
        ok, st, d, brut = thru(conn, [0x1B] + pwd)
        print(f"  PWD_AUTH {label} : {'OK' if ok else 'ECHEC'} -> {brut}")
        if ok:
            print(f"    PACK = {hexs(d)}")
        res[key] = ok
    return res


def verdict(res):
    if not res:
        return "pas de résultat"
    v, s = res.get("version_ok"), res.get("sig_ok")
    if v and s:
        base = "signature plausible : probablement un vrai NXP"
    elif v and not s:
        base = "GET_VERSION imite NXP mais pas de vraie signature : clone très probable"
    elif not v and not s:
        base = "très probablement un clone"
    else:
        base = "indéterminé (signature plausible mais GET_VERSION atypique)"
    auth = []
    if res.get("auth_default"):
        auth.append("PWD_AUTH usine OK")
    if res.get("auth_amiibo"):
        auth.append("PWD_AUTH amiibo OK")
    if res.get("auth_default") is False and res.get("auth_amiibo") is False:
        auth.append("PWD_AUTH refusé (usine et amiibo)")
    return base + (" ; " + ", ".join(auth) if auth else "")


def main():
    rl = readers()
    if not rl:
        print("Aucun lecteur PC/SC détecté.")
        return
    reader = next((r for r in rl if "ACR122" in str(r).upper()), rl[0])
    print(f"Lecteur : {reader}\n")

    resultats = []
    n = 0
    while True:
        rep = input("Pose un tag sur le lecteur, puis Entrée (q pour finir) : ").strip().lower()
        if rep == "q":
            break
        n += 1
        print(f"\n=== Tag #{n} ===")
        conn = connect_retry(reader)
        if conn is None:
            print("  Aucun tag détecté.\n")
            continue
        res = run_tag(conn, reader)
        v = verdict(res)
        print(f"  >>> Verdict : {v}\n")
        resultats.append((n, hexs(res.get("uid", [])), v))
        try:
            conn.disconnect()
        except Exception:
            pass
        input("Retire le tag (Entrée pour continuer)... ")

    if resultats:
        print("\n=== Récapitulatif ===")
        for n, uid, v in resultats:
            print(f"#{n}  UID {uid}  ->  {v}")


if __name__ == "__main__":
    main()
