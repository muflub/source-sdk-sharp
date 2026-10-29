// A C callback with no user-data argument, bound to an object: the engine and tier0 take
// plain function pointers for the spew hook, the assert notify and the global ConVar change
// callback, and this hands them one that calls a method of the instance that owns it.
//
// The pointer is a small x86-64 stub in a page this object owns: it moves the arguments one
// place along, loads the instance as the first argument and jumps to Trampoline. The page is
// written while read-write and only then made read-execute, never both. Uninstall the
// pointer wherever it was installed before this object is destroyed.

#pragma once

#include <cstddef>
#include <cstdint>
#include <cstring>
#include <type_traits>

#if !defined( __x86_64__ ) && !defined( _M_X64 )
#error "CBoundCallback writes x86-64 code; this target has no thunk"
#endif

// The page holding a thunk: allocated read-write, filled, then made read-execute. NULL when
// the system refuses either step. Defined in bound_callback.cpp.
void *BoundCallback_MapCode( const unsigned char *pCode, size_t nBytes );
void BoundCallback_Unmap( void *pPage );

namespace bound_callback
{

enum class ArgClass
{
	Integer,
	Float
};

template <typename T>
constexpr ArgClass ClassOf()
{
	static_assert( std::is_integral<T>::value || std::is_enum<T>::value || std::is_pointer<T>::value ||
					   std::is_reference<T>::value || std::is_floating_point<T>::value,
				   "a bound callback passes only integers, enums, pointers, references and floats" );
	return std::is_floating_point<T>::value ? ArgClass::Float : ArgClass::Integer;
}

// The thunk's bytes for a signature of these argument classes, or 0 when it cannot be built
// (too many arguments for registers). bWindows picks the Win64 convention over System V.
size_t Assemble( const ArgClass *pArgs, int nArgs, bool bWindows, uint64_t uInstance, uint64_t uTarget,
				 unsigned char *pOut, size_t nOut );

} // namespace bound_callback

template <typename Sig>
class CBoundCallback;

template <typename R, typename... A>
class CBoundCallback<R( A... )>
{
public:
	typedef R ( *Fn )( A... );

	// Binds pfnTarget( pInstance, args... ) behind a plain Fn.
	CBoundCallback( void *pInstance, R ( *pfnTarget )( void *, A... ) )
	{
		static constexpr bound_callback::ArgClass kArgs[sizeof...( A ) + 1] = { bound_callback::ClassOf<A>()...,
																				bound_callback::ArgClass::Integer };

		unsigned char code[64];
#if defined( _WIN32 )
		const bool bWindows = true;
#else
		const bool bWindows = false;
#endif
		const size_t nBytes =
			bound_callback::Assemble( kArgs, (int)sizeof...( A ), bWindows, (uint64_t)(uintptr_t)pInstance,
									  (uint64_t)(uintptr_t)pfnTarget, code, sizeof( code ) );
		m_pPage = nBytes ? BoundCallback_MapCode( code, nBytes ) : nullptr;
	}

	~CBoundCallback() { BoundCallback_Unmap( m_pPage ); }

	CBoundCallback( const CBoundCallback & ) = delete;
	CBoundCallback &operator=( const CBoundCallback & ) = delete;

	// The plain function pointer, or NULL when the thunk could not be made.
	Fn Get() const { return reinterpret_cast<Fn>( m_pPage ); }

	// Forgets the page without unmapping it: for a pointer still installed somewhere this
	// object cannot uninstall it from. The thunk keeps calling the instance it was bound
	// to, so that instance must outlive every call.
	void Abandon() { m_pPage = nullptr; }

	// The target a method of T is bound through: CBoundCallback<Sig>::Trampoline<T, &T::M>.
	template <typename T, R ( T::*M )( A... )>
	static R Trampoline( void *pInstance, A... args )
	{
		return ( static_cast<T *>( pInstance )->*M )( args... );
	}

private:
	void *m_pPage = nullptr;
};
