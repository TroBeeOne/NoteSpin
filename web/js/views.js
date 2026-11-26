// The views of the cards to choose from (Settings → Spinner → Style). Each is a CardView (cardview.js): the same
// cards, turning and clicking, shown its own way. The first is the default. A new view: a file of its own, and a
// line here.

import {WheelView} from './wheelview.js';
import {ScrollView} from './scrollview.js';

export const VIEWS = {
  wheel: {label: 'Card wheel', View: WheelView, hasBase: true,
    hint: 'The cards stand on a wheel with a base: the next ones wait behind the front card, and the one before it lies fallen in front.'},
  scroll: {label: 'Scroll', View: ScrollView, hasBase: false,
    hint: 'Just the cards, rolling round like a list: the ones before the front card curve away above it, and the next ones below.'},
};

export const DEFAULT_VIEW = 'wheel';
