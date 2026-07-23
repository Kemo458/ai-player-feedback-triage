import { useEntities } from '../lib/queries';
import { EmptyState, ErrorState, LoadingState } from './ui';

export function EntitiesPanel({
  gameId,
  source,
  selected,
  onSelect,
}: {
  gameId: string;
  source?: string;
  selected?: string;
  onSelect: (entity: string) => void;
}) {
  const query = useEntities(gameId, { source });

  return (
    <section className="panel">
      <div className="panel-head">
        <h3>Key entities</h3>
        <span className="sub">most mentioned</span>
      </div>
      <div className="panel-pad">
        {query.isLoading ? (
          <LoadingState label="Loading entities…" />
        ) : query.isError ? (
          <ErrorState error={query.error} onRetry={() => query.refetch()} />
        ) : !query.data || query.data.items.length === 0 ? (
          <EmptyState
            icon="◎"
            title="No entities yet"
            desc="Entities appear once feedback has been analyzed."
          />
        ) : (
          <div className="entity-list">
            {query.data.items.slice(0, 25).map((e) => (
              <button
                aria-pressed={selected === e.normalizedName}
                className={`entity-item ${selected === e.normalizedName ? 'selected' : ''}`}
                key={`${e.type}:${e.normalizedName}`}
                onClick={() => onSelect(e.normalizedName)}
                type="button"
              >
                <div style={{ minWidth: 0, flex: 1 }}>
                  <div className="name">{e.normalizedName}</div>
                  <div className="type">{e.type}</div>
                </div>
                <div className="counts">
                  <div>{e.mentionCount} mentions</div>
                  <div className="faint">{e.feedbackCount} items</div>
                </div>
              </button>
            ))}
          </div>
        )}
      </div>
    </section>
  );
}
