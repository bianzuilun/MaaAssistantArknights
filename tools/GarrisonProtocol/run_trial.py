#!/usr/bin/env python3
"""MAA launcher for GarrisonFarm ``rl_live.py``.

Does not vendor GarrisonFarm or model weights. Points at a user-supplied clone,
forwards MAA ADB/OCR paths, and falls back to the author's shop rule table when
no ``ppo_*`` checkpoint is present.
"""

from __future__ import annotations

import argparse
import os
import runpy
import sys
from typing import Any

STOP_NAME = "rl_stop"


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    p = argparse.ArgumentParser(description="Run GarrisonFarm rl_live.py under MAA.")
    p.add_argument("--farm", required=True, help="GarrisonFarm clone directory")
    p.add_argument("--maa", required=True, help="MAA install / working directory")
    p.add_argument("--adb", required=True, help="adb executable")
    p.add_argument("--address", required=True, help="adb device address")
    p.add_argument(
        "--net", default="", help="GF_NET checkpoint (no extension, or .onnx)"
    )
    p.add_argument(
        "--dry-run", action="store_true", help="validate and print plan, do not play"
    )
    p.add_argument("games_cmd", nargs="?", default="games")
    p.add_argument("games", nargs="?", default="1")
    p.add_argument("bonds", nargs="?", default="")
    return p.parse_args(argv)


def _farm_ok(farm: str) -> str | None:
    need = (
        os.path.join(farm, "rl_live.py"),
        os.path.join(farm, "garrison_farm.py"),
        os.path.join(farm, "garrison_sim", "rl", "gfpolicy.py"),
        os.path.join(farm, "garrison_sim", "rl", "gfdata.json"),
    )
    for path in need:
        if not os.path.isfile(path):
            return path
    return None


class RulePolicy:
    """Author rule table: leader core bond scoring, even-turn shop upgrade, sell guard."""

    def __init__(self, net_path: str, gfdata_path: str) -> None:
        import gfpolicy

        self.gd = gfpolicy.GameData(gfdata_path)
        self.sess = None
        self.layers: list[Any] = []
        _ = net_path

    def forward(self, x: Any) -> Any:
        import numpy as np

        return np.zeros(19, np.float32)

    def act(
        self, s: dict[str, Any], ban: tuple[int, ...] = ()
    ) -> tuple[int, Any, Any, float]:
        import gfpolicy
        import numpy as np

        v = gfpolicy.View(self.gd, s)
        mask = gfpolicy.legal_mask(self.gd, s, v)
        for b in ban:
            mask[b] = False
        dummy = np.ones(gfpolicy.N_ACT, np.float32)
        dummy = np.where(mask, dummy, 0)
        total = dummy.sum()
        dummy = dummy / total if total else dummy

        rnd = int(s.get("round", 1))
        if mask[gfpolicy.LEVEL] and rnd in (2, 4, 6, 8):
            return gfpolicy.LEVEL, mask, dummy, 0.0

        best_i, best_sc = None, float("-inf")
        for i in range(5):
            if not mask[gfpolicy.BUY0 + i]:
                continue
            sc = float(gfpolicy.slot_score(self.gd, s, v, s["shop"][i]))
            if sc > best_sc:
                best_i, best_sc = gfpolicy.BUY0 + i, sc
        if best_i is not None and np.isfinite(best_sc) and best_sc > 0:
            return best_i, mask, dummy, 0.0
        if mask[gfpolicy.REFRESH]:
            return gfpolicy.REFRESH, mask, dummy, 0.0
        return gfpolicy.END, mask, dummy, 0.0


def _install_rule_policy() -> None:
    import gfpolicy

    gfpolicy.SELL_GUARD = True
    gfpolicy.Policy = RulePolicy  # type: ignore[misc, assignment]


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    farm = os.path.abspath(args.farm)
    missing = _farm_ok(farm)
    if missing:
        print(f"GarrisonFarm file missing: {missing}", file=sys.stderr)
        return 2

    net = (args.net or "").strip()
    games_n = str(args.games or "1")
    bonds = (args.bonds or "").strip()

    os.environ["GF_MAA"] = os.path.abspath(args.maa)
    os.environ["GF_ADB"] = args.adb
    os.environ["GF_ADDRESS"] = args.address
    os.environ.setdefault("PYTHONUTF8", "1")
    os.environ.setdefault("PYTHONUNBUFFERED", "1")

    if net:
        os.environ["GF_NET"] = net
        plan = f"model {net}"
    else:
        os.environ.pop("GF_NET", None)
        plan = "rule table (no model)"

    print(
        f"GarrisonProtocol trial: farm={farm} maa={args.maa} adb={args.adb} address={args.address} {plan}"
    )
    if args.dry_run:
        print("dry-run ok")
        return 0

    stop_file = os.path.join(farm, STOP_NAME)
    if os.path.isfile(stop_file):
        os.remove(stop_file)

    os.chdir(farm)
    sys.path.insert(0, farm)
    sys.path.insert(0, os.path.join(farm, "garrison_sim", "rl"))
    if not net:
        _install_rule_policy()

    sys.argv = ["rl_live.py", "games", games_n]
    if bonds:
        sys.argv.append(bonds)
    runpy.run_path(os.path.join(farm, "rl_live.py"), run_name="__main__")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
