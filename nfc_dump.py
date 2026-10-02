#!/usr/bin/env python3
"""
Dump en lecture seule d'un tag NTAG215 avec un ACR122 (pyscard).
N'écrit RIEN sur le tag.

Affiche une carte des pages non nulles (pour voir jusqu'où une écriture est allée),
et enregistre le dump complet dans un fichier .bin et .txt à côté du script.

Usage : python nfc_dump.py
"""
import argparse
import time
from smartcard.System import readers

TOTAL_PAGES = 135  # 0x00 à 0x86


def hexs(b):
    return " ".join(f"{x:02X}" for x in b)


def tx(conn, apdu):
    data, sw1, sw2 = conn.transmit(apdu)
    return data, (sw1 << 8) | sw2


def thru(conn, payload):
    cmd = [0xD4, 0x42] + payload
    apdu = [0xFF, 0x00, 0x00, 0x00, len(cmd)] + cmd
    try:
        data, sw = tx(conn, apdu)
    except Exception:
        return False, []
    if sw != 0x9000 or len(data) < 3 or data[0] != 0xD5 or data[1] != 0x43 or data[2] != 0x00:
        return False, []
    return True, list(data[3:])


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


def ranges(pages):
    """[4,5,6,9] -> '04-06, 09'"""
    out, start, prev = [], None, None
    for p in pages:
        if start is None:
            start = prev = p
        elif p == prev + 1:
            prev = p
        else:
            out.append((start, prev))
            start = prev = p
    if start is not None:
        out.append((start, prev))
    return ", ".join(f"{a:02X}" if a == b else f"{a:02X}-{b:02X}" for a, b in out) or "aucune"


def main():
    parser = argparse.ArgumentParser(description="Dump NTAG215 via ACR122")
    parser.add_argument("--no-prompt", action="store_true",
                        help="ne demande pas d'appuyer sur Entree avant la lecture")
    args = parser.parse_args()

    rl = readers()
    if not rl:
        print("Aucun lecteur PC/SC détecté.")
        return
    reader = next((r for r in rl if "ACR122" in str(r).upper()), rl[0])
    print(f"Lecteur : {reader}")
    if not args.no_prompt:
        input("Pose le tag sur le lecteur, puis Entrée : ")
    conn = connect_retry(reader)
    if conn is None:
        print("Aucun tag détecté.")
        return

    pages = {}
    for p in range(0, TOTAL_PAGES, 4):
        ok, d = thru(conn, [0x30, p])
        if not ok or len(d) < 16:
            print(f"Lecture impossible à la page {p:02X} (tag protégé en lecture ?).")
            break
        for i in range(4):
            if p + i < TOTAL_PAGES:
                pages[p + i] = d[4 * i:4 * i + 4]

    if not pages:
        return

    user = [p for p in range(4, 130) if p in pages and pages[p] != [0, 0, 0, 0]]
    print(f"\nPage 3 (CC)            : {hexs(pages[3]) if 3 in pages else '?'}")
    print(f"Pages 4-129 non nulles  : {len(user)} sur 126")
    print(f"Plages non nulles       : {ranges(user)}")
    if user:
        print(f"Dernière page non nulle : {user[-1]:02X} (décimal {user[-1]})")
    for p, label in ((0x82, "Verrous dynamiques"), (0x83, "CFG0"), (0x84, "CFG1"),
                     (0x85, "PWD (toujours illisible)"), (0x86, "PACK (toujours illisible)")):
        if p in pages:
            print(f"Page {p:02X} {label:26}: {hexs(pages[p])}")

    base = "dump_" + "".join(f"{x:02X}" for x in (pages[0][:3] + pages[1])) if 1 in pages else "dump"
    raw = bytearray()
    lines = []
    for p in sorted(pages):
        raw += bytes(pages[p])
        lines.append(f"{p:02X}: {hexs(pages[p])}")
    with open(base + ".bin", "wb") as f:
        f.write(raw)
    with open(base + ".txt", "w") as f:
        f.write("\n".join(lines) + "\n")
    print(f"\nDump enregistré : {base}.bin et {base}.txt")
    print("(Ne colle ici que la carte des pages ci-dessus, pas le contenu du dump.)")


if __name__ == "__main__":
    main()
