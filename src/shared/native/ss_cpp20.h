//========= SourceSharp — C++20 library facilities, for a C++17 tree ==========//
//
// Every target builds as C++17. Where a C++20 library facility says a thing
// better than the C++17 spelling of it, the facility is here under `ss::`, with
// C++20's exact contract, so code can be written the way it will read once the
// tree moves up. Under C++20 each name IS the standard one (a using-
// declaration), so moving up changes no behaviour and the shim can be deleted
// by rewriting `ss::` to `std::`.
//
// Not added to `std`: adding declarations there is undefined behaviour.
//
// What is here, and the contract each keeps (C++20 [container.erasure]):
//
//   ss::erase( c, value )     removes EVERY element equal to value
//   ss::erase_if( c, pred )   removes every element pred accepts
//
// for std::vector, std::deque, std::basic_string and std::list (order of the
// survivors kept), and ss::erase_if for the ordered and unordered maps and sets.
// Each returns how many elements it removed. ss_cpp20_test holds these to that
// contract under C++17, and compiles the same facts again under C++20, where
// the names are std's.
//
//=============================================================================//

#ifndef SOURCESHARP_SS_CPP20_H
#define SOURCESHARP_SS_CPP20_H
#ifdef _WIN32
#pragma once
#endif

#include <algorithm>
#include <deque>
#include <iterator>
#include <list>
#include <map>
#include <set>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>

namespace ss
{

#if defined( __cpp_lib_erase_if ) && __cpp_lib_erase_if >= 202002L

using std::erase;
using std::erase_if;

#else

namespace detail
{
// The sequence containers: remove/remove_if, then one erase of the tail.
template < class C, class U >
typename C::size_type EraseValue( C &c, const U &value )
{
	auto it = std::remove( c.begin(), c.end(), value );
	auto n = static_cast< typename C::size_type >( std::distance( it, c.end() ) );
	c.erase( it, c.end() );
	return n;
}

template < class C, class Pred >
typename C::size_type ErasePred( C &c, Pred pred )
{
	auto it = std::remove_if( c.begin(), c.end(), pred );
	auto n = static_cast< typename C::size_type >( std::distance( it, c.end() ) );
	c.erase( it, c.end() );
	return n;
}

// The associative containers: walk, erasing in place.
template < class C, class Pred >
typename C::size_type EraseNodes( C &c, Pred pred )
{
	const typename C::size_type before = c.size();
	for ( auto it = c.begin(); it != c.end(); )
	{
		if ( pred( *it ) )
			it = c.erase( it );
		else
			++it;
	}
	return before - c.size();
}
} // namespace detail

template < class T, class A, class U >
typename std::vector< T, A >::size_type erase( std::vector< T, A > &c, const U &value )
{
	return detail::EraseValue( c, value );
}

template < class T, class A, class Pred >
typename std::vector< T, A >::size_type erase_if( std::vector< T, A > &c, Pred pred )
{
	return detail::ErasePred( c, pred );
}

template < class T, class A, class U >
typename std::deque< T, A >::size_type erase( std::deque< T, A > &c, const U &value )
{
	return detail::EraseValue( c, value );
}

template < class T, class A, class Pred >
typename std::deque< T, A >::size_type erase_if( std::deque< T, A > &c, Pred pred )
{
	return detail::ErasePred( c, pred );
}

template < class Ch, class Tr, class A, class U >
typename std::basic_string< Ch, Tr, A >::size_type erase( std::basic_string< Ch, Tr, A > &c, const U &value )
{
	return detail::EraseValue( c, value );
}

template < class Ch, class Tr, class A, class Pred >
typename std::basic_string< Ch, Tr, A >::size_type erase_if( std::basic_string< Ch, Tr, A > &c, Pred pred )
{
	return detail::ErasePred( c, pred );
}

// std::list::remove/remove_if return void before C++20; the count is the change
// in size.
template < class T, class A, class U >
typename std::list< T, A >::size_type erase( std::list< T, A > &c, const U &value )
{
	const typename std::list< T, A >::size_type before = c.size();
	c.remove_if( [&value]( const T &elem ) { return elem == value; } );
	return before - c.size();
}

template < class T, class A, class Pred >
typename std::list< T, A >::size_type erase_if( std::list< T, A > &c, Pred pred )
{
	const typename std::list< T, A >::size_type before = c.size();
	c.remove_if( pred );
	return before - c.size();
}

template < class K, class V, class Cmp, class A, class Pred >
typename std::map< K, V, Cmp, A >::size_type erase_if( std::map< K, V, Cmp, A > &c, Pred pred )
{
	return detail::EraseNodes( c, pred );
}

template < class K, class V, class Cmp, class A, class Pred >
typename std::multimap< K, V, Cmp, A >::size_type erase_if( std::multimap< K, V, Cmp, A > &c, Pred pred )
{
	return detail::EraseNodes( c, pred );
}

template < class K, class Cmp, class A, class Pred >
typename std::set< K, Cmp, A >::size_type erase_if( std::set< K, Cmp, A > &c, Pred pred )
{
	return detail::EraseNodes( c, pred );
}

template < class K, class Cmp, class A, class Pred >
typename std::multiset< K, Cmp, A >::size_type erase_if( std::multiset< K, Cmp, A > &c, Pred pred )
{
	return detail::EraseNodes( c, pred );
}

template < class K, class V, class H, class E, class A, class Pred >
typename std::unordered_map< K, V, H, E, A >::size_type erase_if( std::unordered_map< K, V, H, E, A > &c, Pred pred )
{
	return detail::EraseNodes( c, pred );
}

template < class K, class H, class E, class A, class Pred >
typename std::unordered_set< K, H, E, A >::size_type erase_if( std::unordered_set< K, H, E, A > &c, Pred pred )
{
	return detail::EraseNodes( c, pred );
}

#endif

} // namespace ss

#endif // SOURCESHARP_SS_CPP20_H
