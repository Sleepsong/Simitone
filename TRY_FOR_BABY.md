# Try for Baby

Simitone adds **Try for Baby**, from The Sims 2, to The Sims 1. It's WooHoo with a chance of a baby:

- **50%** of tries bring a baby, and **1 in 10** of those babies are twins.
- There's no "do you want a baby?" question. The baby simply arrives in its bassinet, and the game asks you to
  name it. Twins arrive one after the other, and you name each.
- Otherwise there's no baby this time, and a message says so.

## Using it

The Sims 1's WooHoo is **Play** in the **Vibromatic Heart Bed** (Livin' Large), so Try for Baby appears on that bed,
next to Play. Choose it for one partner, and for the other as you would for Play. It appears when the Sim:

- is an adult in the household that lives on this lot,
- has a partner there of the other sex (as in The Sims 2), and
- has a daily relationship of at least 50 with that partner, both ways.

The household must also have fewer than 8 members. The game's own rules for Play still apply on top.

Play itself still works as it always has, and so does a kiss. Either can still bring the game's baby question by
chance, as in The Sims 1.

## How it works

The Sims 1's baby lives in the game's own scripts, which Simitone runs. Try for Baby doesn't change the game's files.
When a menu is first opened on a lot, it finds three things in the scripts:

- **The baby question:** a yes/no dialog whose text mentions a baby. The search reads the dialog's text, so it needs
  the game's English text.
- **The interactions that lead to it:** the bed's Play, and the kiss.
- **The scripts in between**, where the game rolls the dice for a baby.

When a Sim starts Try for Baby, Simitone rolls once for the couple:

- **The dice:** the rolls in those in-between scripts are steered to pass (baby) or fail (no baby).
- **The question:** it's answered "yes" or "no" for them, without being shown. A baby then goes through the game's
  usual code: the bassinet, then naming.
- **Twins:** the game's baby code runs a second time.

The new interaction is a copy of the bed's Play, kept in memory only, and never saved to any file.

The log shows what was found and what happened. Lines start with `[baby]`:

- the baby dialogs found, and the interactions that lead to them,
- each try and its roll, the dice it steered, the questions it answered, and how it ended.

The engine side is `FSO.SimAntics.Model.TS1Platform.VMTryForBaby` in FreeSO. It hooks:

- the random number primitive,
- the dialog primitive,
- the start of queued interactions,
- pie menus.

Simitone's lot screen shows the message when a try ends with twins or with no baby.

## Limits

- It needs the Vibromatic Heart Bed, the only bed in The Sims 1 with WooHoo.
- It finds the baby question by its English text. With the game in another language, it can't answer the question.
- With the desktop `-jit` option, compiled scripts don't go through the steered dice rolls.
- There's no pregnancy. A successful try goes straight to the baby, as The Sims 1's own babies do.
