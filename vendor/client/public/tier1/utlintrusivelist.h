//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Purpose: Intrusive linked list templates, both for singly and doubly linked lists
//
// $Revision: $
// $NoKeywords: $
//===========================================================================//

#ifndef UTILINTRUSIVELIST_H
#define UTILINTRUSIVELIST_H

#ifdef _WIN32
#pragma once
#endif

#include "tier0/basetypes.h"
#include "utlmemory.h"
#include "tier0/dbg.h"


// 
// These templates are used for intrusive linked list classes. Intrusive linked list templates
// force the structs and classes contained within them to have their own m_pNext, (optionally),
// m_pPrev, and other fields contained within. All memory management is up to the caller and their
// classes. No data will ever be copied. Nodes can only exist on one list at a time, because of
// only having on m_Next field, and manipulating the list while walking it requires that care on
// the part of the caller. All accessing and searching functions work by passing and returning
// pointers.
//
//
//
// naming and field conventions:
// functions referring to a DList are for doubly linked lists. nodes must have m_pHead and
// m_pPrev pointer fields.
// Functions using Priority require an m_Priority field, which must be comparable.
//
// Some functions are mean for use with lists which maintain both a head and tail pointer
// in order to support fast adding to the end.


/// validates that the doubly linked list has the proper structure, pointer-wise

namespace IntrusiveList
{
#ifdef SUPERSLOW_DEBUG_VERSION                              
	template<class T> inline void ValidateDList(T *head)
	{                                                           
		if (head)
		{
			Assert(head->m_pPrev==0);
		}
		while(head)
		{
			if (head->m_pNext)
			{
				Assert(head->m_pNext->m_pPrev==head);
			}
			if (head->m_pPrev)
			{
				Assert(head->m_pPrev->m_pNext==head);
			}
			head=head->m_pNext;
		}
	}
#else
	template<class T> inline void ValidateDList(T * /*head*/)
	{
	}
#endif

	


    

	// removes node 'which' from doubly linked list with 'head'
	template<class T> inline void RemoveFromDList(T * &head, T *which)
	{
		if (which->m_pPrev)
		{
			Assert(which->m_pPrev->m_pNext==which);
			which->m_pPrev->m_pNext=which->m_pNext;
			if (which->m_pNext)
			{
				Assert(which->m_pNext->m_pPrev==which);
				which->m_pNext->m_pPrev=which->m_pPrev;
			}
		}
		else
		{
			if (head==which)
			{
				head=which->m_pNext;
				if (head)
				{
					Assert(head->m_pPrev==which);
					head->m_pPrev=0;
				}
			}
		}
		which->m_pNext=which->m_pPrev=0;
		ValidateDList(head);
  
	}


	// add a node to the end of a singly linked list
	template<class T> void AddToDTail(T * & head, T * node)
	{
		node->m_pNext=0;
		if (! head)
		{
			head=node;
		}
		else
		{
			T *ptr=head;
			while(ptr->m_pNext)
			{
				ptr=ptr->m_pNext;
			}
			ptr->m_pNext=node;
			node->m_pPrev=ptr;   //
		}
	}

    // add a node to end of doubly linked list.
	template<class T> inline void AddToDHead(T * &head, T *which)
	{
		which->m_pNext=head;
		if (head)
		{
			head->m_pPrev=which;
		}
		which->m_pPrev=0;
		head=which;
		ValidateDList(head);
	}

	// add a node to front of doubly linked list which maintains a tail ptr
	template<class T> inline void AddToDHeadWithTailPtr(T * &head, T *which, T * &tailptr)
	{
		which->m_pNext=head;
		if (head)
		{
			head->m_pPrev=which;
		}
		else
		{
			tailptr=which;
		}
		which->m_pPrev=0;
		head=which;
		ValidateDList(head);
	}

	// add a node to end of doubly linked list which maintains a tail ptr
	template<class T> inline void AddToDTailWithTailPtr(T * &head, T *which, T * & tailptr)
	{
		if (! tailptr)
		{
			Assert(! head);
			which->m_pPrev=which->m_pNext=0;
			tailptr=head=which;
		}
		else
		{
			which->m_pNext=0;
			which->m_pPrev=tailptr;
			tailptr->m_pNext=which;
			tailptr=which;
		}
		ValidateDList( head );
	}
	
	// Remove a node from a dlist , maintaining the tail ptr. node is not 'delete' d
	template<class T> inline void RemoveFromDListWithTailPtr(T * &head, T *which, T * & tailptr)
	{
		if (which==tailptr)
		{
			tailptr=which->m_pPrev;
		}
		if (which->m_pPrev)
		{
			Assert(which->m_pPrev->m_pNext==which);
			which->m_pPrev->m_pNext=which->m_pNext;
			if (which->m_pNext)
			{
				Assert(which->m_pNext->m_pPrev==which);
				which->m_pNext->m_pPrev=which->m_pPrev;
			}
		}
		else
		{
			if (head==which)
			{
				head=which->m_pNext;
				if (head)
				{
					Assert(head->m_pPrev==which);
					head->m_pPrev=0;
				}
			}
		}
		which->m_pNext=which->m_pPrev=0;
		ValidateDList(head);
  
	}







	// Remove from a singly linked list. no delete called.
	template<class T,class V> void RemoveFromList(T * & head, V *which)
	{
		if (head==which)
		{
			head=which->m_pNext;
		}
		else
		{
			for(T * i=head; i; i=i->m_pNext)
			{
				if (i->m_pNext==which)
				{
					i->m_pNext=which->m_pNext;
					return;
				}
			}
		}
	}



	// find the Nth node in a list. null if index too high.
	template<class T> T *NthNode(T * head, int idx)
	{
		while(idx && head)
		{
			idx--;
			head=head->m_pNext;
		}
		return head;
	}

	//Add a node to the head of a singly-linked
	// Note that the head var passed to this will be modified.
	template<class T,class V> static inline void AddToHead(T * & head, V * node)
	{
		node->m_pNext=head;
		head=node;
	}  

	//Add a node to the tail of a singly-linked. Not fast
	// Note that the head var passed to this will be modified.
	template<class T,class V> static inline void AddToTail(T * & head, V * node)
	{
		node->m_pNext = NULL;
		if ( ! head )
			head = node;
		else
		{
			T *pLastNode = head;
			while( pLastNode->m_pNext )
				pLastNode = pLastNode->m_pNext;
			pLastNode->m_pNext = node;
		}
	}  

	//Add a node to the head of a singly-linked list, maintaining a tail pointer
	template<class T,class V> static inline void AddToHead(T * & head, T * &tail,V * node)
	{
		if (! head)
		{
			tail=node;
		}
		node->m_pNext=head;
		head=node;
	}  



	// return the node in head before in a singly linked list. returns null if head is empty, n is
	// null, or if n is the first node. not fast.
	template<class T> static inline T * PrevNode(T *head, T *node)
	{
		T *i;
		for(i=head;i;i=i->m_pNext)
		{
			if (i->m_pNext == node)
				break;
		}
		return i;
	}

  



	// count # of elements in list
	template<class T> int ListLength(T *head)
	{
		int len=0;
		while(head)
		{
			len++;
			head=head->m_pNext;
		}
		return len;
	}

	// this will kill a list if the list is of objects which automatically
	// remove themselves from the list when delete is called
	template<class T> void KillList(T * & head)
	{
		while(head)
		{
			delete head;
		}
	}




	template <class T> static inline T * FindNamedNodeCaseSensitive(T * head, char const *name)
	{
		for(;head && strcmp(head->m_Name,name); head=head->m_pNext)
		{
		}
		return head;
	}






	// reverse a singly linked list. not recommended for anything other than valve programming
	// interview :-)
	template <class T> T *ReversedList( T * head )
	{
		T * pNewHead=NULL;
		while( head )
		{
			T *pNext=head->m_pNext;
#ifdef INTERVIEW_QUESTION
			head->m_pNext=pNewHead;
			pNewHead = head;
#else
			AddToHead( pNewHead, head );
#endif
			head = pNext;
		}
		return pNewHead;
	}
};

// singly linked list
template<class T> class CUtlIntrusiveList
{
public:
	T *m_pHead;

	FORCEINLINE T *Head( void ) const
	{
		return m_pHead;
	}
	
	FORCEINLINE CUtlIntrusiveList(void)
	{
		m_pHead = NULL;
	}


	FORCEINLINE void RemoveAll( void )
	{
		// empty list. doesn't touch nodes at all
		m_pHead = NULL;
	}
	FORCEINLINE void AddToHead( T * node )
	{
		IntrusiveList::AddToHead( m_pHead, node );
	}

	FORCEINLINE void AddToTail( T * node )
	{
		IntrusiveList::AddToTail( m_pHead, node );
	}

	void RemoveNode(T *which)
	{
		IntrusiveList::RemoveFromList( m_pHead, which );
	}

	// this will kill a list if the list is of objects which automatically
	// remove themselves from the list when delete is called
	void KillList( void )
	{
		while(m_pHead)
		{
			delete m_pHead;
		}
	}


	// return the node in head before in a singly linked list. returns null if head is empty, n is
	// null, or if n is the first node. not fast. Fast for dlists
	T * PrevNode(T *node)
	{
		return IntrusiveList::PrevNode( m_pHead, node );
	}

	int NthNode( int n )
	{
		return NthNode( m_pHead, n );
	}

	// this will kill all elements in a list if
	// the elements are of a type which does NOT remove itself from
	// the list when the destructor is called.
	void Purge( void )
	{
		while (m_pHead)
		{
			T* tmp=m_pHead->m_pNext;
			delete m_pHead;
			m_pHead=tmp;
		}
	}

	int Count( void ) const
	{
		return IntrusiveList::ListLength( m_pHead );
	}

	FORCEINLINE T * FindNamedNodeCaseSensitive( char const *pName ) const
	{
		return IntrusiveList::FindNamedNodeCaseSensitive( m_pHead, pName );

	}

	T *RemoveHead( void )
	{
		if ( m_pHead )
		{
			T *pRet = m_pHead;
			m_pHead = pRet->m_pNext;
			return pRet;
		}
		else
			return NULL;
	}
};

// doubly linked list
template<class T> class CUtlIntrusiveDList : public CUtlIntrusiveList<T>
{
public:

	FORCEINLINE void AddToHead( T * node )
	{
		IntrusiveList::AddToDHead( CUtlIntrusiveList<T>::m_pHead, node );
	}
	FORCEINLINE void AddToTail( T * node )
	{
		IntrusiveList::AddToDTail( CUtlIntrusiveList<T>::m_pHead, node );
	}

	void RemoveNode(T *which)
	{
		IntrusiveList::RemoveFromDList( CUtlIntrusiveList<T>::m_pHead, which );
	}

	T *RemoveHead( void )
	{
		if ( CUtlIntrusiveList<T>::m_pHead )
		{
			T *pRet = CUtlIntrusiveList<T>::m_pHead;
			CUtlIntrusiveList<T>::m_pHead = CUtlIntrusiveList<T>::m_pHead->m_pNext;
			if ( CUtlIntrusiveList<T>::m_pHead )
				CUtlIntrusiveList<T>::m_pHead->m_pPrev = NULL;
			return pRet;
		}
		else
			return NULL;
	}

	T * PrevNode(T *node)
	{
		return ( node )?node->m_Prev:NULL;
	}

};

template<class T> class CUtlIntrusiveDListWithTailPtr : public CUtlIntrusiveDList<T>
{
public:

	T *m_pTailPtr;

	FORCEINLINE CUtlIntrusiveDListWithTailPtr( void ) : CUtlIntrusiveDList<T>()
	{
		m_pTailPtr = NULL;
	}

	FORCEINLINE void AddToHead( T * node )
	{
		IntrusiveList::AddToDHeadWithTailPtr( CUtlIntrusiveList<T>::m_pHead, node, m_pTailPtr );
	}
	FORCEINLINE void AddToTail( T * node )
	{
		IntrusiveList::AddToDTailWithTailPtr( CUtlIntrusiveList<T>::m_pHead, node, m_pTailPtr );
	}

	void RemoveNode( T *pWhich )
	{
		IntrusiveList::RemoveFromDListWithTailPtr( CUtlIntrusiveList<T>::m_pHead, pWhich, m_pTailPtr );
	}

	void Purge( void )
	{
		CUtlIntrusiveList<T>::Purge();
		m_pTailPtr = NULL;
	}

	void Kill( void )
	{
		CUtlIntrusiveList<T>::Purge();
		m_pTailPtr = NULL;
	}

	T *RemoveHead( void )
	{
		if ( CUtlIntrusiveDList<T>::m_pHead )
		{
			T *pRet = CUtlIntrusiveDList<T>::m_pHead;
			CUtlIntrusiveDList<T>::m_pHead = CUtlIntrusiveDList<T>::m_pHead->m_pNext;
			if ( CUtlIntrusiveDList<T>::m_pHead )
				CUtlIntrusiveDList<T>::m_pHead->m_pPrev = NULL;
			if (! CUtlIntrusiveDList<T>::m_pHead )
				m_pTailPtr = NULL;
			ValidateDList( CUtlIntrusiveDList<T>::m_pHead );
			return pRet;
		}
		else
			return NULL;
	}

	T * PrevNode(T *node)
	{
		return ( node )?node->m_Prev:NULL;
	}

};


#endif
