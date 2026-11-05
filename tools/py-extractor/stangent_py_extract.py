#!/usr/bin/env python3
"""stangent-py-extract: run by `stangent graph build` for each Python project (D127).

    python3 -I -B stangent_py_extract.py --root <repo> --project <id> --name <name> --dir <project dir> [--no-flask]

Reads the project's files, relative to the repo, one per line on stdin, and prints its fragment
as JSON lines on stdout (docs/code-graph.md). Standard library only.

It only ever parses. Nothing from the code it reads is imported or run: `ast.parse` builds a tree
and that is all. Python's calls can't be resolved the way a compiler resolves C#'s, so this says
only what the text proves, and reports the rest:

  - certain (0): one binding in scope, in the same module or through a relative import.
  - guessed (1): an absolute import (where it lands depends on how the app is started), a type hint
    (never enforced), or a value returned by a function.
  - outside: the call lands in a package or a built-in.
  - not in the project: the receiver's type isn't known, but no project code has that name.
  - unresolved: a blind spot. Never a guess at a target.
"""
import argparse
import ast
import builtins
import json
import os
import re
import sys
import tokenize
import warnings

PY = "python"
FLASK = "flask-routes"
FLASK_APPS = {"flask.Flask", "flask.app.Flask"}
FLASK_BLUEPRINTS = {"flask.Blueprint", "flask.blueprints.Blueprint"}
REST_PACKAGES = ("flask_restful", "flask_restx", "flask_restplus", "flask_smorest")
HTTP_METHODS = ("get", "post", "put", "patch", "delete")
ALL_VIEW_METHODS = HTTP_METHODS + ("head", "options")
DYNAMIC = {"getattr", "setattr", "eval", "exec", "__import__", "globals", "locals", "vars"}
BUILTINS = set(dir(builtins))
STDLIB = set(getattr(sys, "stdlib_module_names", sys.builtin_module_names))
# One <converter:name> or <name>. The argument list of a converter, as in <any(a,b):name>, is allowed.
RULE_PART = re.compile(r"<(?:(?P<conv>[A-Za-z_]\w*)(?:\([^>]*\))?:)?(?P<name>[A-Za-z_]\w*)>")


class Output:
    def __init__(self):
        self.lines = []
        self.nodes = set()
        self.edges = set()
        self.blinds = set()

    def node(self, id, kind, name, project, file, line=0, end=0, is_test=False):
        if id in self.nodes:
            return
        self.nodes.add(id)
        self.lines.append({"t": "node", "id": id, "kind": kind, "name": name, "project": project, "file": file,
                           "line": line, "endLine": end, "isTest": is_test})

    def edge(self, source, target, kind, certain=True, detector=PY):
        if source == target or (source, target, kind) in self.edges:
            return
        self.edges.add((source, target, kind))
        self.lines.append({"t": "edge", "source": source, "target": target, "kind": kind, "label": 0 if certain else 1, "detector": detector})

    def blind(self, node, file, line, reason, detector=PY):
        key = (node, file, line, reason)
        if key in self.blinds:
            return False
        self.blinds.add(key)
        self.lines.append({"t": "blind", "node": node, "file": file, "line": line, "reason": reason, "detector": detector})
        return True

    def fact(self, detector, kind, node, file, line, data):
        self.lines.append({"t": "fact", "detector": detector, "kind": kind, "node": node, "file": file, "line": line, "data": data})

    def warning(self, message):
        self.lines.append({"t": "warning", "message": message})


class Sym:
    """A point on the map."""

    def __init__(self, id, kind, name, module, node, parent):
        self.id, self.kind, self.name, self.module, self.node, self.parent = id, kind, name, module, node, parent
        self.scope = None  # its own scope, for a class or a function

    @property
    def is_class(self):
        return self.kind == "type"

    @property
    def is_callable(self):
        return self.kind in ("method", "property")


class Binding:
    """What one assignment, def or import says about a name."""

    def __init__(self, kind, sym=None, expr=None, scope=None, stmt=None, name=None, annotation=None):
        self.kind, self.sym, self.expr, self.scope, self.stmt, self.name, self.annotation = kind, sym, expr, scope, stmt, name, annotation


class Scope:
    def __init__(self, kind, module, parent, sym=None):
        self.kind, self.module, self.parent, self.sym = kind, module, parent, sym
        self.bindings = {}
        self.declared_outer = {}  # name -> "global" or "nonlocal"
        self.stars = []
        self.instance_attrs = {}  # on a class: self.x = expr, from any of its methods

    def bind(self, name, binding):
        self.bindings.setdefault(name, []).append(binding)

    @property
    def key(self):
        return self.sym.id if self.sym else "file:" + self.module.rel


class Val:
    """What an expression is known to be. `kind`: sym, inst, module, ext, literal, super."""

    __slots__ = ("kind", "sym", "qual", "mod", "certain", "key", "expr", "via", "scope")

    def __init__(self, kind, sym=None, qual=None, mod=None, certain=True, key=None, expr=None, via=None, scope=None):
        self.kind, self.sym, self.qual, self.mod, self.certain = kind, sym, qual, mod, certain
        self.key, self.expr, self.via, self.scope = key, expr, via, scope

    def but(self, **changes):
        fields = {f: getattr(self, f) for f in self.__slots__}
        fields.update(changes)
        return Val(**fields)

    def identity(self):
        return (self.kind, self.sym.id if self.sym else None, self.qual, self.mod)


class Module:
    def __init__(self, rel, tree):
        self.rel, self.tree = rel, tree
        self.scope = Scope("module", self, None)
        self.file_id = "file:" + rel
        self.name = os.path.splitext(os.path.basename(rel))[0]


def read_source(path):
    # tokenize.open honours a coding cookie and a BOM, as Python itself does.
    with tokenize.open(path) as f:
        return f.read()


def line_of(node):
    return getattr(node, "lineno", 0)


def span(node):
    first = min([line_of(d) for d in getattr(node, "decorator_list", [])] + [line_of(node)])
    return first, getattr(node, "end_lineno", line_of(node)) or line_of(node)


class Project:
    def __init__(self, args, files):
        self.out = Output()
        self.root = os.path.abspath(args.root)
        self.id, self.name = args.project, args.name
        self.dir = os.path.relpath(os.path.abspath(args.dir), self.root).replace(os.sep, "/")
        if self.dir == ".":
            self.dir = ""
        self.flask = not args.no_flask
        self.sources = sorted(f for f in files if not f.endswith(".ipynb"))
        self.sources_set = set(self.sources)
        self.notebooks = sorted(f for f in files if f.endswith(".ipynb"))
        self.modules = {}  # rel -> Module
        self.syms = {}
        self.classes = []
        self.dirs = set()
        self.names_in_project = set()
        self.import_cache = {}
        self.active = set()
        self.returns_cache = {}
        self.counts = dict(callsCertain=0, callsGuessed=0, callsOutside=0, callsNotInProject=0, callsUnresolved=0,
                           importsResolved=0, importsOutside=0, importsUnresolved=0, routes=0, routeProblems=0)
        self.flask_routes = []
        self.registrations = {}  # blueprint key -> list of (parent key or None, prefix or None, prefix known, prefix given)
        self.parents = {}  # blueprint key -> its value, for a blueprint other blueprints register on

    # ---- Declaring ---------------------------------------------------------------------------

    def run(self):
        self.out.node(self.id, "project", self.name, self.id, None)
        unparsed = 0
        for rel in self.sources:
            fileid = "file:" + rel
            self.out.node(fileid, "file", rel, self.id, rel)
            self.out.edge(self.id, fileid, "contains")
            try:
                source = read_source(os.path.join(self.root, rel))
                with warnings.catch_warnings():
                    warnings.simplefilter("ignore")  # invalid escapes and the like are the app's business
                    tree = ast.parse(source, filename=rel)
                module = Module(rel, tree)
                self.declare_body(tree.body, module.scope, fileid)
                self.modules[rel] = module
            except (SyntaxError, ValueError, OverflowError, RecursionError, MemoryError, OSError, LookupError) as e:
                unparsed += 1
                self.modules.pop(rel, None)
                reason = f"Python file couldn't be parsed by this Python ({type(e).__name__}): its code is ❓"
                self.out.blind(fileid, rel, getattr(e, "lineno", 0) or 0, reason)
                self.out.warning(f"{rel}: couldn't be parsed ({type(e).__name__}), so it's ❓")
        for rel in self.sources:
            parts = rel.split("/")[:-1]
            for i in range(1, len(parts) + 1):
                self.dirs.add("/".join(parts[:i]))
        for sym in self.syms.values():
            self.names_in_project.add(sym.name.rsplit(".", 1)[-1])
        for cls in self.classes:
            self.names_in_project.update(cls.scope.instance_attrs)
        for rel, module in self.modules.items():
            try:
                self.walk_body(module.tree.body, module.scope, module.file_id)
            except Exception as e:  # one file's trouble is reported; it never stops the rest
                self.out.blind(module.file_id, rel, 0, f"Python file couldn't be walked ({type(e).__name__}): some of its calls are ❓")
                self.out.warning(f"{rel}: couldn't be walked ({type(e).__name__}), so some of its calls are ❓")
        self.hierarchy()
        if self.flask:
            self.finish_flask()
        for rel in self.notebooks:
            self.out.blind(self.id, rel, 0, "notebook isn't read: code in .ipynb files isn't on the map")
        self.summary(unparsed)
        return self.out

    def summary(self, unparsed):
        data = {"files": str(len(self.sources) - unparsed), "unparsed": str(unparsed), "notebooks": str(len(self.notebooks))}
        data.update({k: str(v) for k, v in self.counts.items()})
        self.out.fact(PY, "summary", self.id, None, 0, data)

    def add_sym(self, kind, name, module, node, scope, owner, is_test=False):
        id = f"py|{module.rel}#{name}"
        existing = self.syms.get(id)
        if existing:  # the same name defined twice, in the two arms of an `if`: one point
            return existing
        sym = Sym(id, kind, name, module, node, owner)
        self.syms[id] = sym
        start, end = span(node)
        self.out.node(id, kind, name, self.id, module.rel, start, end, is_test)
        self.out.edge(owner, id, "contains")
        return sym

    def qual(self, scope, name):
        return f"{scope.sym.name}.{name}" if scope.sym else name

    def declare_body(self, body, scope, owner):
        for stmt in body:
            self.declare(stmt, scope, owner)

    def declare(self, node, scope, owner):
        module = scope.module
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
            self.declare_function(node, scope, owner)
            return
        if isinstance(node, ast.ClassDef):
            self.declare_class(node, scope, owner)
            return
        if isinstance(node, ast.Lambda):
            return  # its own names; the walk gives it a scope
        if isinstance(node, (ast.Global, ast.Nonlocal)):
            for name in node.names:
                scope.declared_outer[name] = "global" if isinstance(node, ast.Global) else "nonlocal"
            return
        if isinstance(node, ast.Import):
            for alias in node.names:
                bound = alias.asname or alias.name.split(".")[0]
                self.bind(scope, bound, Binding("import", stmt=node, name=alias.name if alias.asname else alias.name.split(".")[0], scope=scope))
            return
        if isinstance(node, ast.ImportFrom):
            for alias in node.names:
                if alias.name == "*":
                    scope.stars.append(node)
                else:
                    self.bind(scope, alias.asname or alias.name, Binding("from", stmt=node, name=alias.name, scope=scope))
            return
        if isinstance(node, ast.Assign):
            for target in node.targets:
                if isinstance(target, ast.Name):
                    self.bind_value(scope, target.id, node.value, owner, node)
                else:
                    self.declare_target(target, scope)
            self.declare(node.value, scope, owner)
            return
        if isinstance(node, ast.AnnAssign):
            if isinstance(node.target, ast.Name):
                self.bind_value(scope, node.target.id, node.value, owner, node, node.annotation)
            else:
                self.declare_target(node.target, scope)
            if node.value is not None:
                self.declare(node.value, scope, owner)
            return
        if isinstance(node, ast.NamedExpr):
            self.bind_value(scope, node.target.id, node.value, None, node)
            self.declare(node.value, scope, owner)
            return
        if isinstance(node, ast.Name):
            if not isinstance(node.ctx, ast.Load):
                self.bind(scope, node.id, Binding("unknown"))
            return
        if isinstance(node, ast.ExceptHandler) and node.name:
            self.bind(scope, node.name, Binding("unknown"))
        # match statements capture names as plain strings.
        for capture in ("MatchAs", "MatchStar"):
            cls = getattr(ast, capture, None)
            if cls and isinstance(node, cls) and node.name:
                self.bind(scope, node.name, Binding("unknown"))
        cls = getattr(ast, "MatchMapping", None)
        if cls and isinstance(node, cls) and node.rest:
            self.bind(scope, node.rest, Binding("unknown"))
        for child in ast.iter_child_nodes(node):
            self.declare(child, scope, owner)

    def declare_target(self, target, scope):
        for n in ast.walk(target):
            if isinstance(n, ast.Name) and not isinstance(n.ctx, ast.Load):
                self.bind(scope, n.id, Binding("unknown"))

    def bind(self, scope, name, binding):
        how = scope.declared_outer.get(name)
        if how == "global":
            target = scope.module.scope
            target.bind(name, binding if binding.kind in ("def",) else Binding("unknown"))
            return
        if how == "nonlocal":
            outer = scope.parent
            while outer and outer.kind != "function":
                outer = outer.parent
            if outer:
                outer.bind(name, Binding("unknown"))
                return
        scope.bind(name, binding)

    def bind_value(self, scope, name, value, owner, node, annotation=None):
        sym = None
        if scope.kind in ("module", "class") and owner is not None and name != "__all__" and name not in scope.declared_outer:
            sym = self.add_sym("field", self.qual(scope, name), scope.module, node, scope, owner)
        self.bind(scope, name, Binding("value", sym=sym, expr=value, scope=scope, annotation=annotation))

    def declare_function(self, node, scope, owner):
        module = scope.module
        decorators = node.decorator_list
        # @x.setter continues the property x; @overload only describes the real definition below it.
        accessor = next((d for d in decorators if isinstance(d, ast.Attribute) and d.attr in ("setter", "getter", "deleter")
                         and isinstance(d.value, ast.Name) and d.value.id == node.name), None)
        if any(self.decorator_name(d) == "overload" for d in decorators):
            return
        kind = "property" if any(self.decorator_name(d) in ("property", "cached_property") for d in decorators) or accessor is not None else "method"
        qual = self.qual(scope, node.name)
        is_test = self.is_test(node.name, scope)
        sym = self.add_sym(kind, qual, module, node, scope, owner, is_test)
        fn = Scope("function", module, scope, sym)
        fn.function_node = node
        if sym.scope is None:
            sym.scope = fn
        if accessor is None:
            self.bind(scope, node.name, Binding("def", sym=sym))
        self.declare_params(node.args, fn, scope, decorators)
        for d in decorators:
            self.declare(d, scope, None)
        for default in node.args.defaults + [d for d in node.args.kw_defaults if d is not None]:
            self.declare(default, scope, None)
        # Global and nonlocal first: they change what the assignments before them in the text mean.
        for n in self.own_nodes(node):
            if isinstance(n, (ast.Global, ast.Nonlocal)):
                self.declare(n, fn, sym.id)
        for stmt in node.body:
            self.declare(stmt, fn, sym.id)
        if not hasattr(sym, "extra_scopes"):
            sym.extra_scopes = []
        sym.extra_scopes.append((node, fn, scope))

    def own_nodes(self, function):
        stack = list(function.body)
        while stack:
            n = stack.pop()
            yield n
            if not isinstance(n, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef, ast.Lambda)):
                stack.extend(ast.iter_child_nodes(n))

    def declare_params(self, args, fn, enclosing, decorators):
        params = list(getattr(args, "posonlyargs", [])) + list(args.args)
        names = [d for d in map(self.decorator_name, decorators)]
        first_special = enclosing.kind == "class" and "staticmethod" not in names and params
        for i, p in enumerate(params):
            if i == 0 and first_special:
                is_cls = "classmethod" in names or fn.sym.name.rsplit(".", 1)[-1] in ("__new__", "__init_subclass__", "__class_getitem__")
                fn.bind(p.arg, Binding("cls" if is_cls else "self", sym=enclosing.sym))
            else:
                fn.bind(p.arg, Binding("param", annotation=p.annotation, scope=enclosing))
        for p in list(args.kwonlyargs):
            fn.bind(p.arg, Binding("param", annotation=p.annotation, scope=enclosing))
        for p in (args.vararg, args.kwarg):
            if p:
                fn.bind(p.arg, Binding("param"))

    def decorator_name(self, d):
        if isinstance(d, ast.Name):
            return d.id
        if isinstance(d, ast.Attribute):
            return d.attr
        return None

    def declare_class(self, node, scope, owner):
        module = scope.module
        qual = self.qual(scope, node.name)
        sym = self.add_sym("type", qual, module, node, scope, owner)
        if sym.scope is None:
            sym.scope = Scope("class", module, scope, sym)
            sym.enclosing = scope
            sym.class_node = node
            self.classes.append(sym)
        self.bind(scope, node.name, Binding("def", sym=sym))
        for stmt in node.body:
            self.declare(stmt, sym.scope, sym.id)
        # self.x = value, in any method directly on this class.
        for stmt in node.body:
            if isinstance(stmt, (ast.FunctionDef, ast.AsyncFunctionDef)) and stmt.args.args:
                self_name = stmt.args.args[0].arg
                fn = self.syms.get(f"py|{module.rel}#{qual}.{stmt.name}")
                for n in self.own_nodes(stmt):
                    targets = n.targets if isinstance(n, ast.Assign) else [n.target] if isinstance(n, (ast.AnnAssign, ast.AugAssign)) else []
                    for t in targets:
                        if isinstance(t, ast.Attribute) and isinstance(t.value, ast.Name) and t.value.id == self_name:
                            value = n.value if not isinstance(n, ast.AugAssign) else None
                            sym.scope.instance_attrs.setdefault(t.attr, []).append((value, fn.scope if fn else sym.scope))

    def is_test(self, name, scope):
        if not name.startswith("test"):
            return False
        base = os.path.basename(scope.module.rel)
        in_test_file = base.startswith("test_") or base.endswith("_test.py")
        if scope.kind == "module":
            return in_test_file
        if scope.kind == "class":
            if in_test_file and scope.sym.name.rsplit(".", 1)[-1].startswith("Test"):
                return True
            return any(isinstance(b, ast.Attribute) and b.attr == "TestCase" or isinstance(b, ast.Name) and b.id == "TestCase"
                       for b in scope.sym.node.bases)
        return False

    # ---- Imports -----------------------------------------------------------------------------

    def module_at(self, path):
        """A project module at this path (without extension), or None."""
        for candidate in (path + ".py", path + "/__init__.py"):
            if candidate in self.sources_set:
                return candidate
        if path in self.dirs:
            return "dir:" + path
        return None

    def find_absolute(self, importer, dotted):
        """Every project module this absolute import could be, searching from the importer's folder up to the project's."""
        cache_key = (os.path.dirname(importer), dotted)
        if cache_key in self.import_cache:
            return self.import_cache[cache_key]
        path = dotted.replace(".", "/")
        found = []
        here = os.path.dirname(importer)
        while True:
            hit = self.module_at(f"{here}/{path}" if here else path)
            if hit and hit not in found:
                found.append(hit)
            if here == self.dir or not here:
                break
            here = os.path.dirname(here)
        self.import_cache[cache_key] = found
        return found

    def elsewhere(self, importer, dotted):
        """Project files the import's name fits, off the importer's own path: not provable, so worth saying."""
        if dotted.split(".")[0] in STDLIB:
            return []
        path = dotted.replace(".", "/")
        return sorted(rel for rel in self.sources
                      if rel.endswith(f"/{path}.py") or rel.endswith(f"/{path}/__init__.py") or rel in (f"{path}.py", f"{path}/__init__.py"))

    def module_val(self, target, certain):
        return Val("module", mod=target, certain=certain)

    def resolve_import(self, module, dotted):
        """(Val or None, why not). An import outside the project is an `ext` value."""
        found = self.find_absolute(module.rel, dotted)
        if len(found) == 1:
            return self.module_val(found[0], False), None
        if len(found) > 1:
            return None, f"import {dotted} fits several project files"
        others = self.elsewhere(module.rel, dotted)
        if others:
            return None, f"import {dotted} could be {others[0]}, which isn't on this file's path"
        return Val("ext", qual=dotted), None

    def relative_base(self, module, level, name):
        folder = os.path.dirname(module.rel)
        for _ in range(level - 1):
            folder = os.path.dirname(folder)
        if name:
            return (f"{folder}/{name.replace('.', '/')}" if folder else name.replace(".", "/"))
        return folder

    def from_import(self, module, stmt, name):
        """The value of `from stmt.module import name`, or (None, why)."""
        if stmt.level:
            base = self.relative_base(module, stmt.level, stmt.module)
            target = self.module_at(base) if base else None
            if target is None and base == "":
                target = "dir:"
            if target is None:
                return None, f"relative import .{stmt.module or ''} isn't in the project"
            value = self.attr_of_module(self.module_val(target, True), name)
            if value is None:
                return None, f"{name} isn't in {target.replace('dir:', '')}"
            return value, None
        dotted = stmt.module or ""
        base, why = self.resolve_import(module, dotted)
        if base is None:
            return None, why
        if base.kind == "ext":
            return Val("ext", qual=f"{dotted}.{name}"), None
        value = self.attr_of_module(base, name)
        if value is None:
            return None, f"{name} isn't in {base.mod.replace('dir:', '')}"
        return value, None

    def attr_of_module(self, modval, attr):
        target = modval.mod
        certain = modval.certain
        if not target.startswith("dir:") and target in self.modules:
            scope = self.modules[target].scope
            if attr in scope.bindings or scope.stars:
                value = self.name_in_scope(attr, scope)
                if value is not None:
                    return value.but(certain=value.certain and certain)
                if attr in scope.bindings:
                    return None
        folder = target[4:] if target.startswith("dir:") else (os.path.dirname(target) if target.endswith("/__init__.py") or target == "__init__.py" else None)
        if folder is not None:
            hit = self.module_at(f"{folder}/{attr}" if folder else attr)
            if hit:
                return self.module_val(hit, certain)
        return None

    # ---- What an expression is ---------------------------------------------------------------

    def name_in_scope(self, name, scope):
        bindings = scope.bindings.get(name)
        if bindings:
            return self.from_bindings(name, bindings, scope)
        for star in scope.stars:
            value = self.star_lookup(star, name, scope)
            if value is not None:
                return value
        return None

    def star_lookup(self, stmt, name, scope):
        if name.startswith("_"):
            return None
        value, _ = self.from_import(scope.module, stmt, name)
        return value if value is not None and value.kind != "ext" else None

    def lookup(self, name, scope):
        """Python's own order: this scope, enclosing functions (not classes), the module, then built-ins."""
        here = scope
        first = True
        while here is not None:
            if here.kind != "class" or first:
                if name in here.bindings or here.stars:
                    if name in here.bindings:
                        return self.name_in_scope(name, here), True
                    value = self.name_in_scope(name, here)
                    if value is not None:
                        return value, True
            first = False
            here = here.parent
        if name in BUILTINS:
            return Val("ext", qual="builtins." + name), True
        return None, False

    def from_bindings(self, name, bindings, scope):
        guard = ("name", id(scope), name)
        if guard in self.active:
            return None
        self.active.add(guard)
        try:
            values = [self.from_binding(b, name, scope) for b in bindings]
        finally:
            self.active.discard(guard)
        if any(v is None for v in values):
            return None
        first = values[0]
        if all(v.identity() == first.identity() for v in values):
            return first
        return None  # bound to different things: which one runs isn't provable

    def from_binding(self, b, name, scope):
        if b.kind == "def":
            return Val("sym", sym=b.sym)
        if b.kind == "self":
            return Val("inst", sym=b.sym, key=None)
        if b.kind == "cls":
            return Val("sym", sym=b.sym)
        if b.kind == "unknown":
            return None
        if b.kind == "param":
            return self.from_annotation(b.annotation, b.scope) if b.annotation is not None else None
        if b.kind == "import":
            return self.from_plain_import(b, scope)
        if b.kind == "from":
            value, _ = self.from_import(scope.module, b.stmt, b.name)
            return value
        if b.kind == "value":
            key = f"{scope.key}:{name}"
            value = self.infer(b.expr, b.scope) if b.expr is not None else None
            if value is None and b.annotation is not None:
                value = self.from_annotation(b.annotation, b.scope)
            if value is None:
                return Val("sym", sym=b.sym, key=key, expr=b.expr, scope=b.scope) if b.sym else None
            changes = {"via": value.via or b.sym}
            if value.key is None:
                changes.update(key=key, expr=b.expr, scope=b.scope)
            return value.but(**changes)
        return None

    def from_plain_import(self, b, scope):
        value, _ = self.resolve_import(scope.module, b.name)
        return value

    def from_annotation(self, annotation, scope):
        if annotation is None or scope is None:
            return None
        node = annotation
        if isinstance(node, ast.Constant) and isinstance(node.value, str):
            try:
                node = ast.parse(node.value, mode="eval").body
            except SyntaxError:
                return None
        if isinstance(node, ast.BinOp) and isinstance(node.op, ast.BitOr):
            if isinstance(node.right, ast.Constant) and node.right.value is None:
                node = node.left
            elif isinstance(node.left, ast.Constant) and node.left.value is None:
                node = node.right
            else:
                return None
        if isinstance(node, ast.Subscript) and isinstance(node.value, (ast.Name, ast.Attribute)) and self.decorator_name(node.value) == "Optional":
            node = node.slice
        if not isinstance(node, (ast.Name, ast.Attribute)):
            return None
        value = self.infer(node, scope)
        if value is None:
            return None
        if value.kind == "sym" and value.sym.is_class:
            return Val("inst", sym=value.sym, certain=False)
        if value.kind == "ext":
            return Val("ext", qual=value.qual + "()", certain=False)
        return None

    def infer(self, expr, scope):
        guard = ("expr", id(expr), id(scope))
        if guard in self.active:
            return None
        self.active.add(guard)
        try:
            return self.infer_inner(expr, scope)
        finally:
            self.active.discard(guard)

    def infer_inner(self, expr, scope):
        if isinstance(expr, ast.Name):
            value, _ = self.lookup(expr.id, scope)
            return value
        if isinstance(expr, ast.Attribute):
            return self.attr_of(self.infer(expr.value, scope), expr.attr)
        if isinstance(expr, ast.Call):
            return self.result_of(expr, scope)
        if isinstance(expr, ast.Await):
            return self.infer(expr.value, scope)
        if isinstance(expr, (ast.Constant, ast.JoinedStr, ast.List, ast.Dict, ast.Set, ast.Tuple,
                             ast.ListComp, ast.DictComp, ast.SetComp, ast.GeneratorExp)):
            if isinstance(expr, ast.Constant) and expr.value is None:
                return None
            return Val("literal", qual="builtins.literal")
        if isinstance(expr, ast.BinOp):
            left = self.infer(expr.left, scope)
            return left if left is not None and left.kind == "literal" else None
        return None

    def attr_of(self, recv, attr):
        if recv is None:
            return None
        if recv.kind == "module":
            return self.attr_of_module(recv, attr)
        if recv.kind in ("ext", "literal"):
            return Val("ext", qual=f"{recv.qual}.{attr}", certain=recv.certain)
        if recv.kind == "sym" and recv.sym.is_class:
            return self.member(recv.sym, attr, recv.certain, instance=False)
        if recv.kind == "inst":
            return self.member(recv.sym, attr, recv.certain, instance=True)
        if recv.kind == "super":
            return self.member(recv.sym, attr, recv.certain, instance=True, skip_own=True)
        return None

    def bases_of(self, cls):
        """The class's bases, each a Sym, an outside name or None when unknown."""
        result = []
        for base in cls.class_node.bases:
            value = self.infer(base, cls.enclosing)
            if value is None:
                result.append(None)
            elif value.kind == "sym" and value.sym.is_class:
                result.append(value)
            elif value.kind == "ext":
                result.append(value.qual)
            else:
                result.append(None)
        return result

    def linear(self, cls):
        """The class and everything above it, left to right, depth first, each once. Outside and unknown bases stay in the list, as strings and None."""
        seen, order = set(), []

        def visit(c, certain):
            if c.id in seen:
                return
            seen.add(c.id)
            order.append((c, certain))
            for base in self.bases_of(c):
                if isinstance(base, Val):
                    visit(base.sym, certain and base.certain)
                else:
                    order.append((base, certain))

        visit(cls, True)
        return order

    def member(self, cls, attr, certain, instance, skip_own=False):
        """A class's member by Python's lookup, as far as the text shows it."""
        doubt = False
        unknown = False
        ext = None
        for entry, entry_certain in self.linear(cls):
            if isinstance(entry, Sym):
                if skip_own and entry is cls:
                    continue
                own = self.member_here(entry, attr, instance)
                if own is not None:
                    return own.but(certain=own.certain and certain and entry_certain and not doubt)
            elif entry is None:
                unknown = True
            else:
                ext = ext or entry
                doubt = True
            if unknown:
                return None  # a base nobody can see may define it first
        return Val("ext", qual=f"{ext}.{attr}", certain=certain) if ext else None

    def member_here(self, cls, attr, instance):
        scope = cls.scope
        if attr in scope.bindings:
            return self.name_in_scope(attr, scope)
        if instance and attr in scope.instance_attrs:
            values = scope.instance_attrs[attr]
            if len(values) != 1 or values[0][0] is None:
                return None
            value = self.infer(values[0][0], values[0][1])
            return value
        return None

    def result_of(self, call, scope):
        func = call.func
        if isinstance(func, ast.Name) and func.id == "super":
            builtin, _ = self.lookup("super", scope)
            if builtin is not None and builtin.kind == "ext":
                here = scope
                while here is not None and here.kind != "class":
                    here = here.parent
                if here is not None:
                    return Val("super", sym=here.sym, certain=len(here.sym.node.bases) <= 1)
            return None
        value = self.infer(func, scope)
        if value is None:
            return None
        if value.kind == "ext":
            return Val("ext", qual=value.qual + "()", certain=value.certain)
        if value.kind == "literal":
            return Val("ext", qual="builtins.literal()")
        if value.kind == "sym":
            if value.sym.is_class:
                return Val("inst", sym=value.sym, certain=value.certain)
            if value.sym.kind == "method":
                returned = self.returned_by(value.sym)
                return returned.but(certain=False) if returned is not None else None
        return None

    def returned_by(self, fn):
        """What a function returns, when every `return` in it says the same thing."""
        if fn.id in self.returns_cache:
            return self.returns_cache[fn.id]
        self.returns_cache[fn.id] = None
        if fn.scope is None or not hasattr(fn.scope, "function_node"):
            return None
        returns = [n for n in self.own_nodes(fn.scope.function_node) if isinstance(n, ast.Return) and n.value is not None]
        values = [self.infer(r.value, fn.scope) for r in returns]
        result = None
        if values and all(v is not None for v in values) and all(v.identity() == values[0].identity() for v in values):
            result = values[0]
        self.returns_cache[fn.id] = result
        return result

    # ---- Walking: lines from code -------------------------------------------------------------

    def walk_body(self, body, scope, owner):
        for stmt in body:
            self.walk(stmt, scope, owner)

    def walk(self, node, scope, owner):
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
            self.walk_function(node, scope, owner)
            return
        if isinstance(node, ast.ClassDef):
            self.walk_class(node, scope)
            return
        if isinstance(node, ast.Lambda):
            inner = Scope("function", scope.module, scope, scope.sym)
            self.declare_params(node.args, inner, inner, [])
            self.declare(node.body, inner, None)
            for default in node.args.defaults + [d for d in node.args.kw_defaults if d is not None]:
                self.walk(default, scope, owner)
            self.walk(node.body, inner, owner)
            return
        if isinstance(node, ast.Import):
            self.walk_import(node, scope)
            return
        if isinstance(node, ast.ImportFrom):
            self.walk_import_from(node, scope)
            return
        if isinstance(node, ast.Call):
            self.walk_call(node, scope, owner)
            return
        if isinstance(node, (ast.Name, ast.Attribute)) and isinstance(node.ctx, ast.Load):
            self.use(node, scope, owner)
        if isinstance(node, ast.AnnAssign):  # an annotation isn't a call, and often isn't even evaluated
            for part in (node.target, node.value):
                if part is not None:
                    self.walk(part, scope, owner)
            return
        for child in ast.iter_child_nodes(node):
            self.walk(child, scope, owner)

    def use(self, node, scope, owner):
        if isinstance(node, ast.Attribute):
            self.walk(node.value, scope, owner)
        value = self.infer(node, scope)
        if value is None:
            return
        target = value.sym if value.kind == "sym" else None
        if value.via is not None:
            self.out.edge(owner, value.via.id, "uses", value.certain)
        if target is not None:
            self.out.edge(owner, target.id, "uses", value.certain)

    def walk_function(self, node, scope, owner):
        qual = self.qual(scope, node.name)
        sym = self.syms.get(f"py|{scope.module.rel}#{qual}")
        if sym is None:  # an @overload signature
            return
        if scope.kind == "function":
            self.out.edge(scope.sym.id, sym.id, "uses")
        for d in node.decorator_list:
            self.walk_decorator(d, node, scope, sym)
        for default in node.args.defaults + [d for d in node.args.kw_defaults if d is not None]:
            self.walk(default, scope, sym.id)
        fn = next((f for n, f, _ in getattr(sym, "extra_scopes", []) if n is node), None)
        if fn is None:
            return
        self.walk_body(node.body, fn, sym.id)

    def walk_class(self, node, scope):
        qual = self.qual(scope, node.name)
        sym = self.syms[f"py|{scope.module.rel}#{qual}"]
        if scope.kind == "function":
            self.out.edge(scope.sym.id, sym.id, "uses")
        for d in node.decorator_list:
            self.walk(d, scope, sym.id)
        for base, expr in zip(self.bases_of(sym), node.bases):
            if isinstance(base, Val):
                self.out.edge(sym.id, base.sym.id, "inherits", base.certain)
            elif base is None:
                self.out.blind(sym.id, sym.module.rel, line_of(expr), f"base class {ast.unparse(expr)} isn't known")
        self.walk_body(node.body, sym.scope, sym.id)

    def walk_import(self, node, scope):
        module = scope.module
        for alias in node.names:
            value, why = self.resolve_import(module, alias.name)
            self.count_import(value, why, module, node, alias.name)

    def walk_import_from(self, node, scope):
        module = scope.module
        for alias in node.names:
            if alias.name == "*":
                self.walk_star(node, scope)
                continue
            value, why = self.from_import(module, node, alias.name)
            self.count_import(value, why, module, node, alias.name)

    def walk_star(self, node, scope):
        module = scope.module
        if node.level:
            base = self.relative_base(module, node.level, node.module)
            target = self.module_at(base) if base else None
            value, why = (self.module_val(target, True), None) if target else (None, f"relative import .{node.module or ''} isn't in the project")
        else:
            value, why = self.resolve_import(module, node.module or "")
        self.count_import(value, why, module, node, "*")
        if value is not None and value.kind == "ext":
            self.out.blind(module.file_id, module.rel, line_of(node), f"from {node.module} import *: names may come from outside the project")

    def count_import(self, value, why, module, node, name):
        if value is None:
            self.counts["importsUnresolved"] += 1
            self.out.blind(module.file_id, module.rel, line_of(node), why or f"import {name} isn't known")
            return
        if value.via is None and value.kind in ("ext", "literal"):
            self.counts["importsOutside"] += 1
            return
        self.counts["importsResolved"] += 1
        if value.via is not None or value.kind == "sym":
            self.out.edge(module.file_id, (value.via or value.sym).id, "imports", value.certain)
        elif value.kind == "module" and value.mod in self.modules:
            self.out.edge(module.file_id, self.modules[value.mod].file_id, "imports", value.certain)

    def walk_decorator(self, d, node, scope, sym):
        call = d if isinstance(d, ast.Call) else None
        func = call.func if call else d
        if self.flask and call and isinstance(func, ast.Attribute) and func.attr in ("route",) + HTTP_METHODS:
            self.flask_decorator(call, func, node, scope, sym)
        self.walk(d, scope, sym.id)

    def walk_call(self, node, scope, owner):
        func = node.func
        for arg in node.args:
            self.walk(arg, scope, owner)
        for kw in node.keywords:
            self.walk(kw.value, scope, owner)
        if isinstance(func, ast.Attribute):
            self.walk(func.value, scope, owner)
        elif not isinstance(func, ast.Name):
            self.walk(func, scope, owner)
        if self.flask and isinstance(func, ast.Attribute):
            self.flask_call(node, func, scope, owner)
        self.classify_call(node, func, scope, owner)

    def unresolved(self, owner, scope, node, reason):
        self.counts["callsUnresolved"] += 1
        self.out.blind(owner, scope.module.rel, line_of(node), reason)

    def resolved(self, owner, target, certain, kind="calls"):
        self.counts["callsCertain" if certain else "callsGuessed"] += 1
        self.out.edge(owner, target.id, kind, certain)

    def classify_call(self, node, func, scope, owner):
        value = self.infer(func, scope)
        if isinstance(func, ast.Name) and func.id in DYNAMIC and value is not None and value.kind == "ext":
            self.unresolved(owner, scope, node, f"dynamic: {func.id}() finds its target at runtime")
            return
        if value is not None and value.kind == "ext" and value.qual.endswith("importlib.import_module"):
            self.unresolved(owner, scope, node, "dynamic: import_module() finds its target at runtime")
            return
        name = func.id if isinstance(func, ast.Name) else func.attr if isinstance(func, ast.Attribute) else None
        if value is None:
            if isinstance(func, ast.Attribute):
                recv = self.infer(func.value, scope)
                if recv is not None and (recv.kind in ("inst", "super", "module") or recv.kind == "sym" and recv.sym.is_class):
                    self.unresolved(owner, scope, node, f".{func.attr}() on a project value, but what it holds can't be followed")
                elif func.attr in self.names_in_project:
                    self.unresolved(owner, scope, node, f".{func.attr}() on a value whose type isn't known; project code has that name")
                else:
                    self.counts["callsNotInProject"] += 1
            elif isinstance(func, ast.Name):
                self.unresolved(owner, scope, node, f"{name}() isn't provably one function: a parameter, a variable or an import that can't be followed")
            else:
                self.unresolved(owner, scope, node, "call of a computed value")
            return
        if value.via is not None:
            self.out.edge(owner, value.via.id, "uses", value.certain)
        if value.kind in ("ext", "literal"):
            self.counts["callsOutside"] += 1
        elif value.kind == "sym" and value.sym.is_class:
            init = self.member(value.sym, "__init__", value.certain, instance=True)
            self.out.edge(owner, value.sym.id, "uses", value.certain)
            if init is not None and init.kind == "sym":
                self.resolved(owner, init.sym, init.certain)
            else:  # the class itself is the target: nothing in the project runs but its name
                self.counts["callsCertain" if value.certain else "callsGuessed"] += 1
        elif value.kind == "sym" and value.sym.is_callable:
            self.resolved(owner, value.sym, value.certain)
        elif value.kind == "inst":
            call = self.member(value.sym, "__call__", value.certain, instance=True)
            if call is not None and call.kind == "sym":
                self.resolved(owner, call.sym, call.certain)
            else:
                self.unresolved(owner, scope, node, f"{name}() calls a value whose __call__ isn't known")
        else:
            if value.kind == "sym":
                self.out.edge(owner, value.sym.id, "uses", value.certain)
            self.unresolved(owner, scope, node, f"{name}() calls a value, not a known function")

    # ---- Classes ------------------------------------------------------------------------------

    def hierarchy(self):
        """A method on a subclass overrides the one above it, and a self.x() call on the base may land on it."""
        for cls in self.classes:
            for entry, certain in self.linear(cls)[1:]:
                if not isinstance(entry, Sym):
                    continue
                for name, bindings in cls.scope.bindings.items():
                    here = next((b.sym for b in bindings if b.kind == "def" and b.sym and b.sym.is_callable), None)
                    above = next((b.sym for b in entry.scope.bindings.get(name, []) if b.kind == "def" and b.sym and b.sym.is_callable), None)
                    if here and above:
                        self.out.edge(here.id, above.id, "overrides", certain)
                        self.out.edge(above.id, here.id, "dispatches-to", certain)

    # ---- Flask --------------------------------------------------------------------------------

    def flask_kind(self, value):
        """"app" or "blueprint" when this value is provably one."""
        if value is None:
            return None
        if value.kind == "ext" and value.qual.endswith("()"):
            q = value.qual[:-2]
            return "app" if q in FLASK_APPS else "blueprint" if q in FLASK_BLUEPRINTS else None
        if value.kind == "inst":
            for entry, _ in self.linear(value.sym):
                if isinstance(entry, str):
                    if entry in FLASK_APPS:
                        return "app"
                    if entry in FLASK_BLUEPRINTS:
                        return "blueprint"
        return None

    def problem(self, owner, scope, node, reason):
        if self.out.blind(owner, scope.module.rel, line_of(node), reason, FLASK):
            self.counts["routeProblems"] += 1

    def flask_decorator(self, call, func, node, scope, sym):
        recv = self.infer(func.value, scope)
        kind = self.flask_kind(recv)
        if kind is None:
            if func.attr == "route":
                text = ast.unparse(func.value)
                if recv is not None and recv.kind == "ext":
                    self.problem(sym.id, scope, call, f"route on {recv.qual}, which isn't Flask: this framework isn't read")
                else:
                    self.problem(sym.id, scope, call, f"route on {text}, which isn't provably a Flask app or blueprint")
            return
        rule = self.arg(call, 0, "rule")
        text = self.const_string(rule, scope)
        if text is None:
            self.problem(sym.id, scope, call, "route's URL is built at runtime")
            return
        methods = None
        if func.attr == "route":
            given = keyword(call, "methods")
            methods = ["GET"]
            if given is not None:
                methods = self.const_strings(given, scope)
                if methods is None:
                    self.problem(sym.id, scope, call, "route's methods are built at runtime, so any verb may match")
                    methods = ["ANY"]
        else:
            methods = [func.attr.upper()]
        self.flask_routes.append(dict(view=sym, recv=recv, kind=kind, rule=text, methods=methods, line=line_of(call), scope=scope, certain=recv.certain))

    def flask_call(self, call, func, scope, owner):
        attr = func.attr
        if attr == "register_blueprint":
            self.flask_register(call, func, scope, owner)
        elif attr == "add_url_rule":
            self.flask_add_url_rule(call, func, scope, owner)
        elif attr == "add_resource":
            recv = self.infer(func.value, scope)
            if recv is not None and recv.kind == "ext" and recv.qual.split(".")[0] in REST_PACKAGES:
                self.problem(owner, scope, call, f"{recv.qual.split('.')[0]} routes aren't read")

    def flask_register(self, call, func, scope, owner):
        arg = self.arg(call, 0, "blueprint")
        child = self.infer(arg, scope) if arg is not None else None
        if self.flask_kind(child) != "blueprint" or child.key is None:
            self.problem(owner, scope, call, f"register_blueprint({ast.unparse(arg) if arg is not None else ''}) isn't provably a known blueprint")
            return
        parent = self.infer(func.value, scope)
        parent_key = parent.key if self.flask_kind(parent) == "blueprint" else None
        if parent_key:
            self.parents[parent_key] = parent
        prefix = keyword(call, "url_prefix")
        known = True
        text = None
        if prefix is not None:
            text = self.const_string(prefix, scope)
            known = text is not None
            if not known:
                self.problem(owner, scope, call, "register_blueprint's url_prefix is built at runtime")
        self.registrations.setdefault(child.key, []).append((parent_key, text, known, prefix is not None))

    def flask_add_url_rule(self, call, func, scope, owner):
        recv = self.infer(func.value, scope)
        kind = self.flask_kind(recv)
        if kind is None:
            self.problem(owner, scope, call, f"add_url_rule on {ast.unparse(func.value)}, which isn't provably a Flask app or blueprint")
            return
        rule = self.const_string(self.arg(call, 0, "rule"), scope)
        if rule is None:
            self.problem(owner, scope, call, "add_url_rule's URL is built at runtime")
            return
        view_expr = keyword(call, "view_func")
        if view_expr is None and len(call.args) > 2:
            view_expr = call.args[2]
        if view_expr is None:
            self.problem(owner, scope, call, "add_url_rule has no view_func: the view is set elsewhere by endpoint name")
            return
        given = keyword(call, "methods")
        methods = self.const_strings(given, scope) if given is not None else None
        if given is not None and methods is None:
            self.problem(owner, scope, call, "add_url_rule's methods are built at runtime, so any verb may match")
            methods = ["ANY"]
        # Cls.as_view("name"): the class's own handlers.
        if isinstance(view_expr, ast.Call) and isinstance(view_expr.func, ast.Attribute) and view_expr.func.attr == "as_view":
            cls = self.infer(view_expr.func.value, scope)
            if cls is None or cls.kind != "sym" or not cls.sym.is_class:
                self.problem(owner, scope, call, f"add_url_rule's view {ast.unparse(view_expr.func.value)} isn't a known class")
                return
            self.flask_class_view(cls, rule, methods, recv, kind, call, scope, owner)
            return
        view = self.infer(view_expr, scope)
        if view is None or view.kind != "sym" or not view.sym.is_callable:
            self.problem(owner, scope, call, f"add_url_rule's view {ast.unparse(view_expr)} isn't a known function")
            return
        self.flask_routes.append(dict(view=view.sym, recv=recv, kind=kind, rule=rule, methods=methods or ["GET"], line=line_of(call), scope=scope,
                                      certain=recv.certain and view.certain))

    def flask_class_view(self, cls, rule, methods, recv, kind, call, scope, owner):
        names = []
        declared = cls.sym.scope.bindings.get("methods")
        if methods is None and declared:
            methods = self.const_strings(declared[0].expr, cls.sym.scope)
        for verb in ALL_VIEW_METHODS:
            handler = self.member(cls.sym, verb, cls.certain, instance=True)
            if handler is not None and handler.kind == "sym":
                names.append((verb.upper(), handler.sym, handler.certain))
        if not names:
            dispatch = self.member(cls.sym, "dispatch_request", cls.certain, instance=True)
            if dispatch is None or dispatch.kind != "sym":
                self.problem(owner, scope, call, f"class view {cls.sym.name} has no handler this can see")
                return
            for verb in methods or ["GET"]:
                names.append((verb.upper(), dispatch.sym, dispatch.certain))
        for verb, handler, certain in names:
            if methods is not None and verb not in [m.upper() for m in methods]:
                continue
            self.flask_routes.append(dict(view=handler, recv=recv, kind=kind, rule=rule, methods=[verb], line=line_of(call), scope=scope,
                                          certain=recv.certain and certain))

    def arg(self, call, index, name):
        given = keyword(call, name)
        if given is not None:
            return given
        return call.args[index] if len(call.args) > index and not isinstance(call.args[index], ast.Starred) else None

    def const_string(self, expr, scope):
        """A string the text settles: a literal, a constant followed to its one assignment, or joined pieces of those."""
        if expr is None:
            return None
        if isinstance(expr, ast.Constant):
            return expr.value if isinstance(expr.value, str) else None
        if isinstance(expr, ast.JoinedStr):
            parts = []
            for v in expr.values:
                inner = v.value if isinstance(v, ast.FormattedValue) else v
                part = self.const_string(inner, scope)
                if part is None:
                    return None
                parts.append(part)
            return "".join(parts)
        if isinstance(expr, ast.BinOp) and isinstance(expr.op, ast.Add):
            left, right = self.const_string(expr.left, scope), self.const_string(expr.right, scope)
            return left + right if left is not None and right is not None else None
        return self.through(expr, scope, self.const_string)

    def const_strings(self, expr, scope):
        if isinstance(expr, (ast.List, ast.Tuple, ast.Set)):
            items = [self.const_string(e, scope) for e in expr.elts]
            return [i.upper() for i in items] if all(i is not None for i in items) else None
        return self.through(expr, scope, self.const_strings)

    def through(self, expr, scope, read):
        """Follows a name to the one expression it was assigned, here or through an import."""
        guard = ("const", id(expr))
        if guard in self.active or not isinstance(expr, (ast.Name, ast.Attribute)):
            return None
        self.active.add(guard)
        try:
            value = self.infer(expr, scope)
            return read(value.expr, value.scope) if value is not None and value.expr is not None else None
        finally:
            self.active.discard(guard)

    def blueprint_prefix(self, recv):
        """The url_prefix in Blueprint(...), if any. (text, known)."""
        expr = recv.expr
        if isinstance(expr, ast.Call):
            prefix = keyword(expr, "url_prefix")
            if prefix is None:
                return None, True
            text = self.const_string(prefix, recv.scope)
            return text, text is not None
        return None, False

    def paths_for(self, route):
        """Every (path, certain) this route is served at."""
        rule = route["rule"]
        if route["kind"] == "app":
            return [(join_rule("", rule), route["certain"])]
        return [(join_rule(prefix, rule), certain and route["certain"]) for prefix, certain in self.prefixes(route["recv"], route, set())]

    def prefixes(self, recv, route, seen):
        """A blueprint's prefix for each place it's registered. Registering with url_prefix= replaces the blueprint's own."""
        own, known = self.blueprint_prefix(recv)
        key = recv.key
        view, at = route["view"], At(route["line"])
        if key is None or not known:
            self.problem(view.id, route["scope"], at, "blueprint's url_prefix is built at runtime, so this route's full URL isn't known")
            return [("{prefix}", False)]
        registrations = self.registrations.get(key)
        if not registrations:
            self.problem(view.id, route["scope"], at, f"blueprint {key.split(':')[-1]} isn't seen registered: it may be registered through an import or call this can't follow")
            return [(own or "", False)]
        result = []
        for parent_key, text, reg_known, given in registrations:
            prefix = "{prefix}" if not reg_known else text if given else (own or "")
            if parent_key is None or parent_key in seen:
                result.append((prefix, reg_known))
                continue
            for above, certain in self.prefixes(self.parents[parent_key], route, seen | {key}):
                result.append((above.rstrip("/") + "/" + prefix.lstrip("/"), certain and reg_known))
        return result

    def finish_flask(self):
        seen = set()
        count = 0
        for route in self.flask_routes:
            for path, certain in self.paths_for(route):
                for verb in route["methods"]:
                    key = (verb, path, route["view"].id)
                    if key in seen:
                        continue
                    seen.add(key)
                    count += 1
                    self.out.fact(FLASK, "endpoint", route["view"].id, route["view"].module.rel, route["line"], {
                        "verb": verb.upper(), "path": path, "rule": route["rule"], "label": "0" if certain else "1",
                        "assembly": "python:" + self.dir if self.dir else "python", "project": self.id,
                    })
        self.counts["routes"] = count


def keyword(call, name):
    for kw in call.keywords:
        if kw.arg == name:
            return kw.value
    return None


class At:
    """A line to report, when the node itself is gone."""

    def __init__(self, line):
        self.lineno = line


def join_rule(prefix, rule):
    """Flask's own join, then `<int:id>` becomes `{id}`, and `<path:rest>` becomes the catch-all `{*rest}`."""
    prefix = prefix or ""
    if prefix:
        full = prefix.rstrip("/") + "/" + rule.lstrip("/")
    else:
        full = rule

    def part(m):
        return "{*" + m.group("name") + "}" if m.group("conv") == "path" else "{" + m.group("name") + "}"

    full = RULE_PART.sub(part, full)
    full = "/" + full.strip("/")
    while "//" in full:
        full = full.replace("//", "/")
    return full


def main():
    parser = argparse.ArgumentParser(description="Print a Python project's graph fragment as JSON lines. Parses only: never imports or runs the code.")
    parser.add_argument("--root", required=True)
    parser.add_argument("--project", required=True)
    parser.add_argument("--name", required=True)
    parser.add_argument("--dir", required=True)
    parser.add_argument("--no-flask", action="store_true")
    args = parser.parse_args()
    if sys.version_info < (3, 9):
        print(f"python_too_old: this needs Python 3.9 or newer; it found {sys.version.split()[0]}", file=sys.stderr)
        return 3
    sys.setrecursionlimit(max(sys.getrecursionlimit(), 10000))
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    if hasattr(sys.stdin, "reconfigure"):
        sys.stdin.reconfigure(encoding="utf-8", errors="surrogateescape")
    files = [line.rstrip("\n") for line in sys.stdin if line.strip()]
    out = Project(args, files).run()
    for line in out.lines:
        sys.stdout.write(json.dumps(line, ensure_ascii=False) + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
