#light "off"
module FStar_Pervasives_Native

type 'a option =
| None
| Some of 'a
