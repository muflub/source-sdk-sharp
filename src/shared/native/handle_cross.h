// Typed pointers from the handle table without a reinterpret cast. An entity may
// be an SDK entity or a standalone one, and the only safe route is a static_cast
// to I{Server,Client}Unknown (one single-inheritance chain) followed by virtual
// calls that adjust the address inside the object. Engine-free, so it can be
// tested against mock hierarchies.
#pragma once

// Whether an object is outside the SDK's tree: GetBaseEntity() answers non-NULL
// only for a standalone entity. TPartition is IServerEntity on the server and
// IClientUnknown on the client.
template <class TPartition>
struct SourceSharp_Partition
{
	template <class TObject>
	static auto GameEntityOf(TObject* pObject) -> decltype(static_cast<TPartition*>(pObject)->GetBaseEntity())
	{
		// One single-inheritance chain, so the cast adjusts nothing.
		return static_cast<TPartition*>(pObject)->GetBaseEntity();
	}

	template <class TObject>
	static bool IsOutsideTheSdk(TObject* pObject)
	{
		return GameEntityOf(pObject) != 0;
	}
};

// GetBaseEntity() for an SDK entity, or NULL for anything outside the SDK's tree.
template <class TPartition, class TUnknown>
inline auto SourceSharp_SDKEntityFromUnknown(TUnknown* pUnknown) -> decltype(pUnknown->GetBaseEntity())
{
	if (!pUnknown)
		return 0;

	if (SourceSharp_Partition<TPartition>::IsOutsideTheSdk(pUnknown))
		return 0;

	return pUnknown->GetBaseEntity();
}

// From the handle table's IHandleEntity to the IGameEntity view, or NULL. That
// view is a separate base at a different address, so it must come from the
// object's own GetBaseEntity(), never a cast. Server only.
template <class TGameEntity, class TServerEntity, class THandleEntity>
inline TGameEntity* SourceSharp_CrossHandleToGameEntity(THandleEntity* pHandleEntity)
{
	if (!pHandleEntity)
		return 0;

	// The only cast: everything in the entity list is an IServerEntity on one chain.
	TServerEntity* pServerEntity = static_cast<TServerEntity*>(pHandleEntity);

	return pServerEntity->GetBaseEntity();
}

// As SourceSharp_SDKEntityFromUnknown, from the networkable face. IServerNetworkable
// and IServerEntity are separate bases, so it converts through GetEntityHandle().
template <class TPartition, class TNetworkable>
inline auto SourceSharp_SDKEntityFromNetworkable(TNetworkable* pNetworkable) -> decltype(pNetworkable->GetBaseEntity())
{
	if (!pNetworkable)
		return 0;

	if (SourceSharp_Partition<TPartition>::IsOutsideTheSdk(pNetworkable->GetEntityHandle()))
		return 0;

	return pNetworkable->GetBaseEntity();
}

// From a trace's IHandleEntity to the client shell, or NULL. Static props are
// skipped, and the resolved shell must be the object hit.
template <class TShell, class THandleEntity, class TResolve, class TIsStaticProp>
inline TShell* SourceSharp_CrossHandleToClientShell(THandleEntity* pHandleEntity, TResolve resolve,
													TIsStaticProp isStaticProp)
{
	if (!pHandleEntity)
		return 0;

	if (isStaticProp(pHandleEntity))
		return 0;

	TShell* pShell = resolve(pHandleEntity);
	if (!pShell)
		return 0;

	// An upcast, adjusted by the compiler with the whole layout in view.
	if (static_cast<THandleEntity*>(pShell) != pHandleEntity)
		return 0;

	return pShell;
}
