# Try for Baby

Simitone adds **Try for Baby**, from The Sims 2, to The Sims 1. In The Sims 1, a couple living together can be asked
"have a baby?" at random after a kiss. With Try for Baby, you choose when they try, and each try has a 50% chance.
A successful try leads into The Sims 1's own baby: its question, the baby in a bassinet, and naming the baby, exactly as
when it happens after a kiss.

## Using it

Select one partner, tap the other, and choose **Try for Baby**. It appears when:

- both Sims are adults, one male and one female (as in The Sims 2),
- both are members of the household that lives on this lot,
- the household has fewer than 8 members,
- their daily relationship is at least 50 both ways, and
- the game would let them kiss right now.

The couple kisses, using the game's own kiss, and then:

- **If the try works** (half the time), the game asks whether they want a baby. From there it's The Sims 1's usual baby.
- **If it doesn't**, a message says there's no baby this time, and they can try again later.
- **If the try works but the game's own baby check still says no**, the message says so. That can happen when their
  mood is low or the kiss is turned down.

A kiss that isn't Try for Baby can still bring the baby question by chance, as it always has in The Sims 1.

## How it works

The Sims 1's baby question lives in the game's own scripts, which Simitone runs. Try for Baby doesn't change the game's
files. When a Sim's menu is first opened on a lot, it finds three things in the scripts:

- **The baby question:** a yes/no dialog whose text mentions a baby. The search reads the dialog's text, so it needs
  the game's English text.
- **The interaction that leads to it**, normally the kiss.
- **The scripts in between**, where the game rolls the dice for a baby.

Try for Baby is added to the Sims' interactions as a copy of that kiss. While a couple is trying, the dice rolls in
those in-between scripts come out the way the try's own 50% roll did. A lucky roll passes the game's check, and an
unlucky roll fails it, so a try can't bring a baby by luck either. Rolls go back to normal as soon as the question
comes up or the try ends.

The new interaction is kept in memory only. The game writes Sims' files back when it saves, and they never get it.

The log says what was found and what happened. Lines start with `[baby]`:

- the baby dialogs found, and which one is the question,
- the interaction it starts from, and the chain of scripts in between,
- each try, its roll, the dice it steered, and the outcome.

The engine side is `FSO.SimAntics.Model.TS1Platform.VMTryForBaby` in FreeSO. It hooks the random number primitive,
the dialog primitive, the start of queued interactions, and Sims' pie menus. Simitone's lot screen shows the message
when a try ends without a baby.

## Limits

- It finds the baby question by its English text. With the game in another language, Try for Baby doesn't appear.
- With the desktop `-jit` option, compiled scripts don't go through the steered dice rolls.
- There's no pregnancy. A successful try goes straight to The Sims 1's baby, as a kiss does.
