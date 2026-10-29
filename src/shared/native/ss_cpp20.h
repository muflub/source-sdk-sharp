// C++20 library facilities for a C++17 tree, under `ss::` with C++20's exact contract.
// Under C++20 each name is the standard one, so `ss::` can later become `std::`.
//
//   ss::erase( c, value )     removes every element equal to value
//   ss::erase_if( c, pred )   removes every element pred accepts
//
// Both return how many elements they removed.

#pragma once

#include <algorithm>
#include <deque>
#include <iterator>
#include <list>
#include <map>
#include <set>
#include <string>
#include <unordered_map>
#include <unordered_set>

namespace ss
{

#if defined(__cpp_lib_erase_if) && __cpp_lib_erase_if >= 202002L

using std::erase;
using std::erase_if;

#else

namespace detail
{
// The sequence containers: remove/remove_if, then one erase of the tail.
template <class C, class U>
typename C::size_type EraseValue(C& c, const U& value)
{
	auto it = std::remove(c.begin(), c.end(), value);
	auto n = static_cast<typename C::size_type>(std::distance(it, c.end()));
	c.erase(it, c.end());
	return n;
}

template <class C, class Pred>
typename C::size_type ErasePred(C& c, Pred pred)
{
	auto it = std::remove_if(c.begin(), c.end(), pred);
	auto n = static_cast<typename C::size_type>(std::distance(it, c.end()));
	c.erase(it, c.end());
	return n;
}

// The associative containers: walk, erasing in place.
template <class C, class Pred>
typename C::size_type EraseNodes(C& c, Pred pred)
{
	const typename C::size_type before = c.size();
	for (auto it = c.begin(); it != c.end();)
	{
		if (pred(*it))
			it = c.erase(it);
		else
			++it;
	}
	return before - c.size();
}
} // namespace detail

template <class T, class A, class U>
typename std::vector<T, A>::size_type erase(std::vector<T, A>& c, const U& value)
{
	return detail::EraseValue(c, value);
}

template <class T, class A, class Pred>
typename std::vector<T, A>::size_type erase_if(std::vector<T, A>& c, Pred pred)
{
	return detail::ErasePred(c, pred);
}

template <class T, class A, class U>
typename std::deque<T, A>::size_type erase(std::deque<T, A>& c, const U& value)
{
	return detail::EraseValue(c, value);
}

template <class T, class A, class Pred>
typename std::deque<T, A>::size_type erase_if(std::deque<T, A>& c, Pred pred)
{
	return detail::ErasePred(c, pred);
}

template <class Ch, class Tr, class A, class U>
typename std::basic_string<Ch, Tr, A>::size_type erase(std::basic_string<Ch, Tr, A>& c, const U& value)
{
	return detail::EraseValue(c, value);
}

template <class Ch, class Tr, class A, class Pred>
typename std::basic_string<Ch, Tr, A>::size_type erase_if(std::basic_string<Ch, Tr, A>& c, Pred pred)
{
	return detail::ErasePred(c, pred);
}

// std::list::remove/remove_if return void before C++20; the count is the change
// in size.
template <class T, class A, class U>
typename std::list<T, A>::size_type erase(std::list<T, A>& c, const U& value)
{
	const typename std::list<T, A>::size_type before = c.size();
	c.remove_if([&value](const T& elem) { return elem == value; });
	return before - c.size();
}

template <class T, class A, class Pred>
typename std::list<T, A>::size_type erase_if(std::list<T, A>& c, Pred pred)
{
	const typename std::list<T, A>::size_type before = c.size();
	c.remove_if(pred);
	return before - c.size();
}

template <class K, class V, class Cmp, class A, class Pred>
typename std::map<K, V, Cmp, A>::size_type erase_if(std::map<K, V, Cmp, A>& c, Pred pred)
{
	return detail::EraseNodes(c, pred);
}

template <class K, class V, class Cmp, class A, class Pred>
typename std::multimap<K, V, Cmp, A>::size_type erase_if(std::multimap<K, V, Cmp, A>& c, Pred pred)
{
	return detail::EraseNodes(c, pred);
}

template <class K, class Cmp, class A, class Pred>
typename std::set<K, Cmp, A>::size_type erase_if(std::set<K, Cmp, A>& c, Pred pred)
{
	return detail::EraseNodes(c, pred);
}

template <class K, class Cmp, class A, class Pred>
typename std::multiset<K, Cmp, A>::size_type erase_if(std::multiset<K, Cmp, A>& c, Pred pred)
{
	return detail::EraseNodes(c, pred);
}

template <class K, class V, class H, class E, class A, class Pred>
typename std::unordered_map<K, V, H, E, A>::size_type erase_if(std::unordered_map<K, V, H, E, A>& c, Pred pred)
{
	return detail::EraseNodes(c, pred);
}

template <class K, class H, class E, class A, class Pred>
typename std::unordered_set<K, H, E, A>::size_type erase_if(std::unordered_set<K, H, E, A>& c, Pred pred)
{
	return detail::EraseNodes(c, pred);
}

#endif

} // namespace ss
