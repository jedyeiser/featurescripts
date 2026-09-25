"""Parse data/topsheet_sections.txt (mid-surface sections of the topsheet, fetch_topsheet.py) and undrape
them the way undrape_utils.fs does, in plain numpy:

    x = arc along the target W (here the mid-surface's own Front-plane section, d = 0) minus the alignment's
    y = -(signed mid-surface arc from the centreline w = 0)          (y = -v keeps the flat part right-handed)

Kernel sections are exact; this is an independent check / illustration, not the production code path.
"""
import os
import numpy as np

from style import DATA

ALIGN_X = 885.0   # world X of the alignment mate connector StjLB


class Station:
    pass


def _chain(pieces, tol=2e-3):
    """Chain section pieces (arrays of 3D points, with exact lengths) by their end points."""
    left = list(range(len(pieces)))
    chains = []
    while left:
        # start from the piece end with the smallest w (world Y)
        best = min(left, key=lambda k: min(pieces[k][0][0][1], pieces[k][0][-1][1]))
        pts, ln = pieces[best]
        if pts[-1][1] < pts[0][1]:
            pts = pts[::-1]
        chain = [(pts, ln)]
        left.remove(best)
        grown = True
        while grown:
            grown = False
            end = chain[-1][0][-1]
            for k in left:
                p, l2 = pieces[k]
                if np.linalg.norm(p[0] - end) < tol:
                    chain.append((p, l2)); left.remove(k); grown = True; break
                if np.linalg.norm(p[-1] - end) < tol:
                    chain.append((p[::-1], l2)); left.remove(k); grown = True; break
        chains.append(chain)
    return chains


def _dedupe(pieces):
    out = []
    for p, l in pieces:
        if any(abs(l - l2) < 1e-6 and (np.linalg.norm(p[6] - p2[6]) < 1e-4) for p2, l2 in out):
            continue
        out.append((p, l))
    return out


def load(path=os.path.join(DATA, "topsheet_sections.txt")):
    stations = []
    cur = None
    L = None
    for line in open(path):
        tok = line.split()
        if not tok:
            continue
        if tok[0] == "T":
            L = float(tok[3])
        elif tok[0] == "S":
            cur = Station()
            cur.s = float(tok[1])
            cur.o = np.array([float(v) for v in tok[2:5]])
            cur.t = np.array([float(v) for v in tok[5:8]])
            cur.pieces = []
            stations.append(cur)
        elif tok[0] == "E":
            bar = tok.index("|")
            ln = float(tok[1])
            pts = np.array([float(v) for v in tok[bar + 1:]]).reshape(-1, 3)
            cur.pieces.append((pts, ln))
    stations.sort(key=lambda st: -st.s)          # the path runs tip -> tail; order tail -> tip
    for st in stations:
        st.arc = L - st.s                         # arc along W from its tail end
        t = -st.t                                 # tail -> tip
        st.T = t
        n = np.array([-t[2], 0.0, t[0]])          # surface normal, up (W lies in the XZ plane)
        st.N = n / np.linalg.norm(n)
        st.pieces = _dedupe(st.pieces)
        chains = _chain(st.pieces)
        st.nchains = len(chains)
        st.ok = len(chains) == 1
        # one polyline with arc length scaled per piece to its exact kernel length
        pts, arcs = [], []
        acc = 0.0
        for p, ln in max(chains, key=lambda c: sum(l for _, l in c)):
            seg = np.linalg.norm(np.diff(p, axis=0), axis=1)
            scale = ln / seg.sum() if seg.sum() > 0 else 1.0
            a = acc + np.concatenate([[0], np.cumsum(seg * scale)])
            pts.append(p if not pts else p[1:])
            arcs.append(a if not arcs else a[1:])
            acc = a[-1]
        st.P = np.vstack(pts)
        st.A = np.concatenate(arcs)
        rel = st.P - st.o
        st.w = rel[:, 1]
        st.h = rel @ st.N
        # zero at the centreline crossing w = 0 nearest the reference (smallest |h|)
        zero = None
        for i in range(len(st.w) - 1):
            if (st.w[i] <= 0 < st.w[i + 1]) or (st.w[i] > 0 >= st.w[i + 1]):
                f = (0 - st.w[i]) / (st.w[i + 1] - st.w[i])
                hz = st.h[i] + f * (st.h[i + 1] - st.h[i])
                if zero is None or abs(hz) < zero[1]:
                    zero = (st.A[i] + f * (st.A[i + 1] - st.A[i]), abs(hz))
        st.L = st.A - (zero[0] if zero else st.A[np.argmin(np.abs(st.w))])
        if st.L[-1] < st.L[0]:
            st.L = -st.L
    # alignment: the station arc at world X = ALIGN_X (W rises steadily in X)
    xs = np.array([st.o[0] for st in stations])
    arcs = np.array([st.arc for st in stations])
    a0 = np.interp(ALIGN_X, xs, arcs)
    for st in stations:
        st.x = st.arc - a0
        st.y = -st.L                                # y = -v (unwrapFast convention)
    return stations, L


def point_at(st, yflat):
    """3D mid-surface point of a station at flat y (None when outside the section)."""
    Lq = -yflat
    order = np.argsort(st.L)
    if Lq < st.L[order[0]] or Lq > st.L[order[-1]]:
        return None
    return np.array([np.interp(Lq, st.L[order], st.P[order, k]) for k in range(3)])


def deformation(stations, ys):
    """Lengthwise stretch and shear between consecutive stations along flat lines y = const."""
    nx = len(stations) - 1
    stretch = np.full((len(ys), nx), np.nan)
    shear = np.full((len(ys), nx), np.nan)
    xm = np.zeros(nx)
    for i in range(nx):
        a, b = stations[i], stations[i + 1]
        xm[i] = 0.5 * (a.x + b.x)
        if not (a.ok and b.ok):
            continue
        lf = b.x - a.x
        for j, y in enumerate(ys):
            p, q = point_at(a, y), point_at(b, y)
            if p is None or q is None:
                continue
            d = q - p
            l3 = np.linalg.norm(d)
            stretch[j, i] = lf / l3 - 1
            # section direction at the chord's middle (d/dy of the mid point, two nearby points)
            p1, p2 = point_at(a, y - 0.5), point_at(a, y + 0.5)
            if p1 is None or p2 is None:
                continue
            u = (p2 - p1) / np.linalg.norm(p2 - p1)
            c = abs(np.dot(d / l3, u))
            shear[j, i] = np.degrees(np.arcsin(min(1.0, c)))
    return xm, stretch, shear


if __name__ == "__main__":
    sts, L = load()
    print("stations", len(sts), "L", L, "multi-chain", sum(not s.ok for s in sts))
    hw = [(s.x, s.L.min(), s.L.max()) for s in sts]
    print("flat width range", min(b - a for _, a, b in hw), max(b - a for _, a, b in hw))
