using ClangSharp.Interop;
using Hexa.NET.Utilities;

namespace HexaGen.CppAst.Parsing
{
    public struct CursorKey : IEquatable<CursorKey>
    {
        public ResolverScope scope;
        public CXCursor cursor;
        public CString name;

        public unsafe CursorKey(CppContainerContext context, CXCursor cursor)
        {
            scope = context.Type switch
            {
                CppContainerContextType.Unspecified => ResolverScope.System,
                CppContainerContextType.System => ResolverScope.System,
                CppContainerContextType.User => ResolverScope.User,
                _ => ResolverScope.System,
            };

            while (cursor.Kind == CXCursorKind.CXCursor_LinkageSpec)
            {
                cursor = cursor.SemanticParent;
            }
            this.cursor = cursor;

            using var usr = cursor.Usr;
            var usrCstr = (byte*)clang.getCString(usr);
            var usrLen = Utils.MbStrLen(usrCstr);
            if (usrLen == -1) throw new Exception();
            if (usrLen == 0)
            {
                using var displayName = cursor.DisplayName;
                var displayNameCstr = (byte*)clang.getCString(displayName);
                var displayNameLen = Utils.MbStrLen(displayNameCstr);
                if (displayNameLen == -1) throw new Exception();
                name = new(BumpAllocator.Shared.Alloc((nuint)displayNameLen + 1), displayNameLen);
                Utils.Memcpy(displayNameCstr, name.CStr, displayNameLen + 1);
            }
            else
            {
                name = new(BumpAllocator.Shared.Alloc((nuint)usrLen + 1), usrLen);
                Utils.Memcpy(usrCstr, name.CStr, usrLen + 1);
            }
        }

        public override readonly bool Equals(object? obj)
        {
            return obj is CursorKey key && Equals(key);
        }

        public readonly bool Equals(CursorKey other)
        {
            return other.scope == scope && other.name == name && other.cursor.IsAnonymous == cursor.IsAnonymous && !(cursor.IsAnonymous && cursor.Hash != other.cursor.Hash);
        }

        public override readonly int GetHashCode()
        {
            return HashCode.Combine(scope, name.GetHashCode(), cursor.IsAnonymous, cursor.IsAnonymous ? cursor.Hash : 0);
        }

        public static bool operator ==(CursorKey left, CursorKey right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(CursorKey left, CursorKey right)
        {
            return !(left == right);
        }
    }
}