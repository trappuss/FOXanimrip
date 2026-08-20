# SPDX-License-Identifier: GPL-3.0-or-later
"""
Locating FoxBrowser export sets on disk.

A FoxBrowser export of ``sna2_main0_def`` looks like this::

    sna2_main0_def.fbx              <- the model (also .dae / .obj)
    sna2_main0_def_rig.json         <- bone hashes, rig units, clip info
    sna2_main0_def_source/          <- the untouched .fmdl
    sna2_main0_def_textures/        <- .dds (+ the .ftex/.ftexs they came from)

This module turns a file selection, a folder, or a whole folder tree into a
list of :class:`ExportSet` objects.
"""

from __future__ import annotations

import os

from . import naming

#: Model containers we can hand to a Blender importer, best first.
MODEL_EXTENSIONS = (".fbx", ".dae", ".obj")

#: Directory suffixes FoxBrowser creates beside a model.  Never descend into
#: ``_source``: it holds the original .fmdl, which Blender cannot read, and on
#: a recursive import it would otherwise be reported as a pile of failures.
SOURCE_DIR_SUFFIX = "_source"
TEXTURE_DIR_SUFFIX = "_textures"

#: Directory names skipped entirely during a recursive walk.
_SKIP_DIR_SUFFIXES = (SOURCE_DIR_SUFFIX, TEXTURE_DIR_SUFFIX)

#: A real model export always drops at least one of these beside its FBX -- a
#: rig.json / maps.tsv, or a _source / _textures folder.  Animation clips have
#: none, which is how a recursive scan tells the models apart from the tens of
#: thousands of clip FBXs an all-animations rip writes one per file.  It keys on
#: the sidecars, not folder names, so a renamed export folder is still fine.
_MODEL_SIDECAR_FILES = ("_rig.json", "_maps.tsv")
_MODEL_SIDECAR_DIRS = (SOURCE_DIR_SUFFIX, TEXTURE_DIR_SUFFIX)


def has_model_sidecar(directory, stem):
    """True if *stem*.fbx in *directory* is a real model export (has sidecars),
    not a bare animation clip."""
    for suffix in _MODEL_SIDECAR_FILES:
        if os.path.isfile(os.path.join(directory, stem + suffix)):
            return True
    for suffix in _MODEL_SIDECAR_DIRS:
        if os.path.isdir(os.path.join(directory, stem + suffix)):
            return True
    return False


class ExportSet:
    """One model plus whatever FoxBrowser wrote alongside it."""

    __slots__ = ("model_path", "name", "directory", "rig_json",
                 "textures_dir", "source_dir", "_texture_index", "_map_sidecar")

    def __init__(self, model_path: str):
        self.model_path = os.path.normpath(model_path)
        self.directory = os.path.dirname(self.model_path)
        self.name = os.path.splitext(os.path.basename(self.model_path))[0]

        rig = os.path.join(self.directory, self.name + "_rig.json")
        self.rig_json = rig if os.path.isfile(rig) else ""

        tex = os.path.join(self.directory, self.name + TEXTURE_DIR_SUFFIX)
        self.textures_dir = tex if os.path.isdir(tex) else ""

        src = os.path.join(self.directory, self.name + SOURCE_DIR_SUFFIX)
        self.source_dir = src if os.path.isdir(src) else ""

        self._texture_index = None
        self._map_sidecar = None

    def __repr__(self):  # pragma: no cover - debugging aid
        return "<ExportSet %s>" % self.name

    def map_sidecar(self):
        """``{base_stem: (normal_stem, spec_stem)}`` from ``<name>_maps.tsv``.

        The tool writes this so the normal and spec maps can be wired even when a
        texture came out hash-named and its role is no longer in the file name.
        Keyed and valued by stem (no extension) to match the texture index.
        Empty dict when there is no sidecar."""
        if self._map_sidecar is not None:
            return self._map_sidecar
        out = {}
        path = os.path.join(self.directory, self.name + "_maps.tsv")
        try:
            with open(path, "r", encoding="utf-8") as fh:
                for line in fh.read().splitlines()[1:]:      # skip header
                    cols = line.split("\t")
                    if len(cols) < 3 or not cols[0]:
                        continue
                    stem = lambda s: os.path.splitext(s)[0] if s else ""
                    out[stem(cols[0])] = (stem(cols[1]), stem(cols[2]))
        except OSError:
            pass
        self._map_sidecar = out
        return out

    @property
    def extension(self) -> str:
        return os.path.splitext(self.model_path)[1].lower()

    # -- textures ---------------------------------------------------------

    def texture_index(self):
        """Map of texture base name -> absolute path, built once and cached.

        Two dicts are returned: exact names and digit-normalised names, since
        Fox Engine numbers sibling maps inconsistently (``sna0_cnt1_def_bsm``
        pairs with ``sna0_cnt2_def_nrm``).
        """
        if self._texture_index is not None:
            return self._texture_index

        exact = {}
        fuzzy = {}
        if self.textures_dir and os.path.isdir(self.textures_dir):
            for entry in sorted(os.listdir(self.textures_dir)):
                base, ext = os.path.splitext(entry)
                if ext.lower() not in (".dds", ".png", ".tga", ".tif", ".tiff"):
                    continue
                path = os.path.join(self.textures_dir, entry)
                # First writer wins so .dds beats a converted .png of the same
                # name only if it sorts first; make .dds explicitly preferred.
                if base not in exact or ext.lower() == ".dds":
                    exact[base] = path
                parsed = naming.parse(base)
                key = (naming.normalise(parsed.stem), parsed.code)
                fuzzy.setdefault(key, []).append((base, path))

        self._texture_index = (exact, fuzzy)
        return self._texture_index

    #: Minimum shared prefix, in characters, before the last-resort match is
    #: allowed to fire.  Fox Engine names are long; 6 keeps ``cm_eyes0_def``
    #: reaching ``cm_eyes0_v00_def_srm`` without letting ``sna2`` match ``sna0``.
    FUZZY_MIN_PREFIX = 6

    def find_texture(self, stems, code: str, fuzzy_fallback=True):
        """Best texture of type *code* for any of *stems*.

        Returns ``(base_name, path, exact)`` or ``None``.  ``exact`` is False
        when the match came from the last-resort prefix search, so the caller
        can report the guess instead of silently trusting it.

        Tries, in order: an exact ``<stem>_<code>`` hit, the same with an
        ``_alp`` tail, a digit insensitive match, then all three again against
        progressively shorter stems.  Failing all of that, and only when
        *fuzzy_fallback* is on, the longest shared prefix across every texture
        of that type wins -- but only if it is a strictly unique best and lands
        on an underscore boundary, so a tie never guesses.
        """
        exact, fuzzy = self.texture_index()
        if not exact:
            return None

        tried = set()
        for stem in stems:
            probe = stem
            while probe and probe not in tried:
                tried.add(probe)
                for suffix in ("", "_alp"):
                    key = "%s_%s%s" % (probe, code, suffix)
                    if key in exact:
                        return key, exact[key], True
                candidates = fuzzy.get((naming.normalise(probe), code))
                if candidates:
                    if len(candidates) == 1:
                        return candidates[0][0], candidates[0][1], True
                    best = max(candidates,
                               key=lambda c: _shared_prefix(c[0], stem))
                    return best[0], best[1], True
                probe = naming.shorten(probe)

        if not fuzzy_fallback:
            return None

        pool = []
        for (_norm_stem, norm_code), entries in fuzzy.items():
            if norm_code == code:
                pool.extend(entries)
        if not pool:
            return None

        scored = []
        for stem in stems:
            for base, path in pool:
                n = _shared_prefix(base, stem)
                # Only accept a match that stops on a token boundary, so
                # "sna2_" cannot pull in an unrelated "sna2_something".
                if n < len(stem) and not base[:n].endswith("_"):
                    continue
                scored.append((n, base, path))
        if not scored:
            return None
        scored.sort(key=lambda s: (-s[0], s[1]))
        best_score = scored[0][0]
        if best_score < self.FUZZY_MIN_PREFIX:
            return None
        winners = {s[1] for s in scored if s[0] == best_score}
        if len(winners) != 1:
            return None  # ambiguous: better to wire nothing than the wrong map
        return scored[0][1], scored[0][2], False


def _shared_prefix(a: str, b: str) -> int:
    n = 0
    for ca, cb in zip(a, b):
        if ca != cb:
            break
        n += 1
    return n


# -- gathering ------------------------------------------------------------

def _ext_rank(path: str) -> int:
    ext = os.path.splitext(path)[1].lower()
    try:
        return MODEL_EXTENSIONS.index(ext)
    except ValueError:
        return len(MODEL_EXTENSIONS)


def _is_skipped_dir(dirname: str) -> bool:
    low = dirname.lower()
    return any(low.endswith(s) for s in _SKIP_DIR_SUFFIXES)


def _dedupe(paths, prefer_all_formats: bool):
    """Collapse ``model.fbx`` / ``model.dae`` / ``model.obj`` into one entry.

    FoxBrowser can write several containers for the same model.  Importing all
    of them would give you three copies of the same character.
    """
    if prefer_all_formats:
        return sorted(paths, key=lambda p: (os.path.dirname(p), p))

    best = {}
    for path in paths:
        key = (os.path.dirname(path).lower(),
               os.path.splitext(os.path.basename(path))[0].lower())
        current = best.get(key)
        if current is None or _ext_rank(path) < _ext_rank(current):
            best[key] = path
    return sorted(best.values(), key=lambda p: (os.path.dirname(p), p))


def gather_from_files(filepaths, extensions=MODEL_EXTENSIONS,
                      prefer_all_formats=False):
    """Export sets for an explicit list of model files."""
    keep = [p for p in filepaths
            if os.path.splitext(p)[1].lower() in extensions and os.path.isfile(p)]
    return [ExportSet(p) for p in _dedupe(keep, prefer_all_formats)]


def gather_from_folder(directory, recursive=False, extensions=MODEL_EXTENSIONS,
                       prefer_all_formats=False, max_depth=0):
    """Export sets found in *directory*.

    *max_depth* of 0 means unlimited; 1 means the folder itself only.
    """
    directory = os.path.normpath(directory)
    found = []
    if not os.path.isdir(directory):
        return []

    if not recursive:
        for entry in sorted(os.listdir(directory)):
            path = os.path.join(directory, entry)
            if os.path.isfile(path) and os.path.splitext(entry)[1].lower() in extensions:
                found.append(path)
        return [ExportSet(p) for p in _dedupe(found, prefer_all_formats)]

    base_depth = directory.rstrip(os.sep).count(os.sep)
    for root, dirs, files in os.walk(directory):
        # Prune FoxBrowser's own sidecar folders and anything hidden.
        dirs[:] = sorted(d for d in dirs
                         if not d.startswith(".") and not _is_skipped_dir(d))
        if max_depth:
            depth = root.rstrip(os.sep).count(os.sep) - base_depth
            if depth >= max_depth - 1:
                dirs[:] = []
        for entry in sorted(files):
            if os.path.splitext(entry)[1].lower() in extensions:
                # Recursive scans reach the animation library; keep only real
                # model exports (the ones with sidecars), not bare clip FBXs.
                if not has_model_sidecar(root, os.path.splitext(entry)[0]):
                    continue
                found.append(os.path.join(root, entry))

    return [ExportSet(p) for p in _dedupe(found, prefer_all_formats)]
