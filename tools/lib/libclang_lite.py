"""A minimal ctypes binding of libclang's C API (clang-c/Index.h), for the
vendor tools: parse a translation unit and walk its cursors with their kinds,
USRs, extents and referenced cursors. Only what tools/vendor-strip needs."""
import ctypes
import ctypes.util
import os

_LIB_CANDIDATES = ("/usr/lib/llvm-22/lib/libclang-22.so.1", "/usr/lib/llvm-22/lib/libclang.so.1",
                   "libclang-22.so.1", "libclang.so")


def _load():
    for c in _LIB_CANDIDATES:
        try:
            return ctypes.CDLL(c)
        except OSError:
            continue
    raise OSError("libclang (LLVM 22) not found")


L = _load()


class CXString(ctypes.Structure):
    _fields_ = [("data", ctypes.c_void_p), ("flags", ctypes.c_uint)]


class Cursor(ctypes.Structure):
    _fields_ = [("kind", ctypes.c_int), ("xdata", ctypes.c_int), ("data", ctypes.c_void_p * 3)]


class SourceLocation(ctypes.Structure):
    _fields_ = [("ptr_data", ctypes.c_void_p * 2), ("int_data", ctypes.c_uint)]


class SourceRange(ctypes.Structure):
    _fields_ = [("ptr_data", ctypes.c_void_p * 2), ("begin_int_data", ctypes.c_uint),
                ("end_int_data", ctypes.c_uint)]


VISITOR = ctypes.CFUNCTYPE(ctypes.c_int, Cursor, Cursor, ctypes.c_void_p)


def _fn(name, res, *args):
    f = getattr(L, name)
    f.restype = res
    f.argtypes = list(args)
    return f


createIndex = _fn("clang_createIndex", ctypes.c_void_p, ctypes.c_int, ctypes.c_int)
disposeIndex = _fn("clang_disposeIndex", None, ctypes.c_void_p)
parseTU = _fn("clang_parseTranslationUnit", ctypes.c_void_p, ctypes.c_void_p, ctypes.c_char_p,
              ctypes.POINTER(ctypes.c_char_p), ctypes.c_int, ctypes.c_void_p, ctypes.c_uint, ctypes.c_uint)
disposeTU = _fn("clang_disposeTranslationUnit", None, ctypes.c_void_p)
getTUCursor = _fn("clang_getTranslationUnitCursor", Cursor, ctypes.c_void_p)
visitChildren = _fn("clang_visitChildren", ctypes.c_uint, Cursor, VISITOR, ctypes.c_void_p)
getCString = _fn("clang_getCString", ctypes.c_char_p, CXString)
disposeString = _fn("clang_disposeString", None, CXString)
getCursorUSR = _fn("clang_getCursorUSR", CXString, Cursor)
getCursorSpelling = _fn("clang_getCursorSpelling", CXString, Cursor)
getCursorReferenced = _fn("clang_getCursorReferenced", Cursor, Cursor)
getCursorExtent = _fn("clang_getCursorExtent", SourceRange, Cursor)
getCursorLocation = _fn("clang_getCursorLocation", SourceLocation, Cursor)
getRangeStart = _fn("clang_getRangeStart", SourceLocation, SourceRange)
getRangeEnd = _fn("clang_getRangeEnd", SourceLocation, SourceRange)
getFileName = _fn("clang_getFileName", CXString, ctypes.c_void_p)
getExpansionLocation = _fn("clang_getExpansionLocation", None, SourceLocation,
                           ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(ctypes.c_uint),
                           ctypes.POINTER(ctypes.c_uint), ctypes.POINTER(ctypes.c_uint))
cursorIsNull = _fn("clang_Cursor_isNull", ctypes.c_int, Cursor)
equalCursors = _fn("clang_equalCursors", ctypes.c_uint, Cursor, Cursor)
isDeclaration = _fn("clang_isDeclaration", ctypes.c_uint, ctypes.c_int)
isDefinition = _fn("clang_isCursorDefinition", ctypes.c_uint, Cursor)
getSemanticParent = _fn("clang_getCursorSemanticParent", Cursor, Cursor)
methodIsVirtual = _fn("clang_CXXMethod_isVirtual", ctypes.c_uint, Cursor)
methodIsStatic = _fn("clang_CXXMethod_isStatic", ctypes.c_uint, Cursor)
numOverloaded = _fn("clang_getNumOverloadedDecls", ctypes.c_uint, Cursor)
overloadedDecl = _fn("clang_getOverloadedDecl", Cursor, Cursor, ctypes.c_uint)
lexicalParent = _fn("clang_getCursorLexicalParent", Cursor, Cursor)
specializedTemplate = _fn("clang_getSpecializedCursorTemplate", Cursor, Cursor)
getOverriddenCursors = _fn("clang_getOverriddenCursors", None, Cursor,
                           ctypes.POINTER(ctypes.POINTER(Cursor)), ctypes.POINTER(ctypes.c_uint))
disposeOverriddenCursors = _fn("clang_disposeOverriddenCursors", None, ctypes.POINTER(Cursor))


getCursorDefinition = _fn("clang_getCursorDefinition", Cursor, Cursor)
_getIncludedFile = _fn("clang_getIncludedFile", ctypes.c_void_p, Cursor)


def includedFile(c):
    """The file an inclusion directive cursor names, or None."""
    f = _getIncludedFile(c)
    return text(getFileName(f)) if f else None


def overridden(c):
    """The cursors c overrides (empty for a method that overrides nothing)."""
    arr, n = ctypes.POINTER(Cursor)(), ctypes.c_uint()
    getOverriddenCursors(c, ctypes.byref(arr), ctypes.byref(n))
    out = [Cursor.from_buffer_copy(arr[i]) for i in range(n.value)]
    if n.value:
        disposeOverriddenCursors(arr)
    return out
getNumDiagnostics = _fn("clang_getNumDiagnostics", ctypes.c_uint, ctypes.c_void_p)

# CXTranslationUnit_DetailedPreprocessingRecord | SkipFunctionBodies(no) | KeepGoing
TU_DETAILED_PP = 0x01
TU_KEEP_GOING = 0x200
TU_INCOMPLETE = 0x02

# Cursor kinds (clang-c/Index.h)
K = dict(StructDecl=2, UnionDecl=3, ClassDecl=4, EnumDecl=5, FieldDecl=6, EnumConstantDecl=7,
         FunctionDecl=8, VarDecl=9, ParmDecl=10, TypedefDecl=20, CXXMethod=21, Namespace=22,
         Constructor=24, Destructor=25, ConversionFunction=26, TemplateTypeParameter=27,
         FunctionTemplate=30, ClassTemplate=31, ClassTemplatePartialSpecialization=32,
         TypeAliasDecl=36, CXXBaseSpecifier=44, TypeRef=43, TemplateRef=45, MemberRef=47, OverloadedDeclRef=49,
         DeclRefExpr=101, MemberRefExpr=102, CallExpr=103, MacroDefinition=501, MacroExpansion=502,
         InclusionDirective=503)


def text(cx):
    s = getCString(cx)
    disposeString(cx)
    return s.decode("utf-8", "replace") if s else ""


def usr(c):
    return text(getCursorUSR(c))


def spelling(c):
    return text(getCursorSpelling(c))


def where(loc):
    f, line, col, off = ctypes.c_void_p(), ctypes.c_uint(), ctypes.c_uint(), ctypes.c_uint()
    getExpansionLocation(loc, ctypes.byref(f), ctypes.byref(line), ctypes.byref(col), ctypes.byref(off))
    return (text(getFileName(f)) if f.value else None), line.value, off.value


def parse(index, path, args):
    argv = (ctypes.c_char_p * len(args))(*[a.encode() for a in args])
    tu = parseTU(index, path.encode(), argv, len(args), None, 0,
                 TU_DETAILED_PP | TU_KEEP_GOING)
    if not tu:
        raise RuntimeError("libclang could not parse %s" % path)
    return tu
