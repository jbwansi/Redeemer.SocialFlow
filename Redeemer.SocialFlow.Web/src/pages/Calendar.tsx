import { useState } from 'react'
import { ChevronLeft, ChevronRight } from 'lucide-react'
import type { Post } from '../api/posts'
import { dayKey, timeLabel, timezone } from '../lib/dates'
import { EmptyState, PlatformLabel } from '../components/shared'

export function Calendar({ posts, open }: { posts: Post[]; open: (post: Post) => void }) {
  const [month, setMonth] = useState(
    () => new Date(new Date().getFullYear(), new Date().getMonth(), 1),
  )
  const start = new Date(month.getFullYear(), month.getMonth(), 1 - ((month.getDay() + 6) % 7))
  const days = Array.from(
    { length: 42 },
    (_, index) => new Date(start.getFullYear(), start.getMonth(), start.getDate() + index),
  )
  const scheduled = posts
    .filter((post) => post.scheduledAt)
    .sort((a, b) => Date.parse(a.scheduledAt!) - Date.parse(b.scheduledAt!))
  const inMonth = scheduled.filter((post) => {
    const date = new Date(post.scheduledAt!)
    return date.getMonth() === month.getMonth() && date.getFullYear() === month.getFullYear()
  })
  return (
    <section className="panel calendar-panel">
      <div className="calendar-toolbar">
        <div>
          <h2>{month.toLocaleDateString(undefined, { month: 'long', year: 'numeric' })}</h2>
          <p>
            {inMonth.length} {inMonth.length === 1 ? 'post' : 'posts'} planned · {timezone}
          </p>
        </div>
        <div className="calendar-controls">
          <button
            className="button"
            onClick={() => setMonth(new Date(new Date().getFullYear(), new Date().getMonth(), 1))}
          >
            Today
          </button>
          <button
            className="icon-button"
            aria-label="Previous month"
            onClick={() => setMonth(new Date(month.getFullYear(), month.getMonth() - 1, 1))}
          >
            <ChevronLeft size={20} />
          </button>
          <button
            className="icon-button"
            aria-label="Next month"
            onClick={() => setMonth(new Date(month.getFullYear(), month.getMonth() + 1, 1))}
          >
            <ChevronRight size={20} />
          </button>
        </div>
      </div>
      <div className="calendar-scroll">
        <div className="calendar-weekdays">
          {['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'].map((day) => (
            <span key={day}>{day}</span>
          ))}
        </div>
        <div className="calendar-grid">
          {days.map((day) => (
            <div
              key={dayKey(day)}
              className={`calendar-day ${day.getMonth() !== month.getMonth() ? 'outside-month' : ''}`}
            >
              <time
                dateTime={dayKey(day)}
                className={dayKey(day) === dayKey(new Date()) ? 'today' : ''}
              >
                {day.getDate()}
              </time>
              {scheduled
                .filter((post) => dayKey(new Date(post.scheduledAt!)) === dayKey(day))
                .map((post) => (
                  <button
                    className={`calendar-post calendar-platform-${post.platform}`}
                    key={post.id}
                    onClick={() => open(post)}
                  >
                    <span>{timeLabel(post.scheduledAt!)}</span>
                    <strong>{post.title}</strong>
                    <small>
                      {post.status === 4 ? 'Scheduled' : post.status === 5 ? 'Published' : 'Failed'}
                    </small>
                  </button>
                ))}
            </div>
          ))}
        </div>
      </div>
      <div className="calendar-legend">
        {([1, 2, 3] as const).map((platform) => (
          <PlatformLabel key={platform} platform={platform} />
        ))}
        <span>Dates shown in your local timezone</span>
      </div>
      {!inMonth.length && (
        <EmptyState title="A fresh page for this month">
          Approve a post, then choose a scheduled date to add it to the calendar.
        </EmptyState>
      )}
    </section>
  )
}
